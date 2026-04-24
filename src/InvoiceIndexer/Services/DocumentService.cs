using System.Collections.Concurrent;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Core;
using InvoiceIndexer.Configuration;
using InvoiceIndexer.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Polly;
using UglyToad.PdfPig;

namespace InvoiceIndexer.Services;

public class DocumentService : IDocumentService
{
    private readonly BlobContainerClient _containerClient;
    private readonly ChatClient _chatClient;
    private readonly SearchClient _searchClient;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IndexerConfig config,
        BlobServiceClient blobServiceClient,
        AzureOpenAIClient openAiClient,
        TokenCredential credential,
        ResiliencePipeline pipeline,
        ILogger<DocumentService> logger)
    {
        _containerClient = blobServiceClient.GetBlobContainerClient(config.StorageContainer);
        _chatClient      = openAiClient.GetChatClient(config.OpenAiVisionDeployment);
        _searchClient    = new SearchClient(new Uri(config.SearchEndpoint), config.SearchIndexName, credential);
        _pipeline        = pipeline;
        _logger          = logger;
    }

    // Lists new PDF blobs from storage, skipping already-indexed ones and capping at 500.
    public async Task<IEnumerable<BlobItem>> ReadBlobsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Reading blobs from container {Container}", _containerClient.Name);

        var blobs = new List<BlobItem>();

        await foreach (var blob in _containerClient.GetBlobsAsync(cancellationToken: ct))
        {
            if (!blob.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping non-PDF blob {Name}", blob.Name);
                continue;
            }

            blobs.Add(blob);
        }

        var existingIds = await GetExistingIdsAsync(blobs, ct);
        var newBlobs = blobs.Where(b => !existingIds.Contains(BlobNameToId(b.Name))).Take(500).ToList();

        _logger.LogInformation("Found {Total} PDF blobs — {New} new (capped at 500), {Skipped} already indexed",
            blobs.Count, newBlobs.Count, blobs.Count - newBlobs.Count);

        return newBlobs;
    }

    // Extracts all blobs in parallel, collecting successful results into a bag.
    public async Task<IEnumerable<InvoiceDocument>> ExtractDocumentsAsync(
        IEnumerable<BlobItem> blobs,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting {Count} documents", blobs.Count());

        var documents = new ConcurrentBag<InvoiceDocument>();

        await Parallel.ForEachAsync(blobs,
            new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
            async (blob, token) =>
            {
                try
                {
                    var document = await ExtractSingleDocumentAsync(blob, token);
                    if (document != null)
                        documents.Add(document);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to process {Name}: {Error}", blob.Name, ex.Message);
                }
            });

        _logger.LogInformation("Extracted {Count} documents", documents.Count);
        return documents;
    }

    // Coordinates the four steps for a single blob: download → text → GPT fields → model.
    private async Task<InvoiceDocument?> ExtractSingleDocumentAsync(BlobItem blob, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var pdfBytes = await DownloadBlobAsync(blob, ct);
        var fullText = ExtractTextFromPdf(pdfBytes);
        var fields   = await ExtractFieldsWithGptAsync(fullText, blob.Name, ct);

        if (fields == null) return null;

        var document = BuildInvoiceDocument(blob.Name, fullText, fields.Value);

        _logger.LogInformation("Extracted {Name} in {Ms}ms", blob.Name, sw.ElapsedMilliseconds);
        return document;
    }

    // Downloads the blob into a byte array; streams to avoid large allocations.
    private async Task<byte[]> DownloadBlobAsync(BlobItem blob, CancellationToken ct)
    {
        var blobClient = _containerClient.GetBlobClient(blob.Name);
        await using var stream = await blobClient.OpenReadAsync(cancellationToken: ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    // Extracts raw text from a PDF using PdfPig — local, fast, no API call.
    private string ExtractTextFromPdf(byte[] pdfBytes)
    {
        using var pdf   = PdfDocument.Open(pdfBytes);
        var pages       = pdf.GetPages().ToList();
        var text        = string.Join("\n", pages.Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));

        _logger.LogInformation("PdfPig extracted {Chars} characters from {Pages} pages",
            text.Length, pages.Count);

        return text;
    }

    // Sends raw invoice text to GPT-4o and parses the returned JSON into a JsonElement.
    private async Task<JsonElement?> ExtractFieldsWithGptAsync(
        string fullText, string blobName, CancellationToken ct)
    {
        // truncate to first 1000 chars for extraction — fields are always at the top
        // full text is kept separately in content field for semantic search
        var extractionText = fullText.Length > 1000 ? fullText[..1000] : fullText;

        var prompt = "Extract fields from this invoice and return JSON only:\n" +
                     "{\n" +
                     "  \"customer\": \"full name from Bill To\",\n" +
                     "  \"amount\": total as number,\n" +
                     "  \"discount\": discount percentage as number,\n" +
                     "  \"category\": product category,\n" +
                     "  \"date\": \"YYYY-MM-DD\",\n" +
                     "  \"order_id\": \"order ID string\",\n" +
                     "  \"ship_mode\": \"shipping method\"\n" +
                     "}\n" +
                     "Return null for missing fields. JSON only, no explanation.\n\n" +
                     $"Invoice text:\n{extractionText}";

        _logger.LogInformation("Sending {Chars} characters to GPT-4o for {Name}",
            extractionText.Length, blobName);

        var response = await _pipeline.ExecuteAsync(async t =>
            await _chatClient.CompleteChatAsync(
                new ChatMessage[] { new UserChatMessage(prompt) },
                cancellationToken: t), ct);

        var json = response.Value.Content[0].Text.Trim();

        _logger.LogInformation("GPT-4o response for {Name}: {Json}", blobName, json);

        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence    = json.LastIndexOf("```");
            json = json[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse GPT response for {Name}: {Error}", blobName, ex.Message);
            _logger.LogDebug("Raw GPT response: {Json}", json);
            return null;
        }
    }

    // Maps extracted JSON fields and full text into an InvoiceDocument ready for indexing.
    private InvoiceDocument BuildInvoiceDocument(
        string blobName, string fullText, JsonElement fields)
    {
        var customer = GetString(fields, "customer");
        var orderId  = GetString(fields, "order_id");
        var category = GetString(fields, "category");
        var shipMode = GetString(fields, "ship_mode");
        var amount   = GetDouble(fields, "amount");
        var discount = GetDouble(fields, "discount");
        var date     = GetDate(fields, "date");

        _logger.LogInformation(
            "Extracted fields for {Name} — customer: {Customer}, amount: {Amount}, " +
            "discount: {Discount}, category: {Category}, date: {Date}",
            blobName, customer, amount, discount, category, date);

        var content = $"Customer: {customer}\n"    +
                      $"Date: {date:yyyy-MM-dd}\n" +
                      $"Amount: ${amount}\n"       +
                      $"Discount: {discount}%\n"   +
                      $"Category: {category}\n"    +
                      $"Order ID: {orderId}\n"     +
                      $"Ship Mode: {shipMode}\n"   +
                      $"Full invoice:\n{fullText}";

        return new InvoiceDocument
        {
            Id         = BlobNameToId(blobName),
            SourceFile = blobName,
            Customer   = customer,
            Amount     = amount,
            Discount   = discount,
            Category   = category,
            Date       = date,
            OrderId    = orderId,
            ShipMode   = shipMode,
            Content    = content
        };
    }

    // Base64-encodes the blob name so it's safe to use as an Azure AI Search document key.
    private static string BlobNameToId(string blobName) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(blobName))
            .Replace("+", "-").Replace("/", "_").Replace("=", "");

    // Queries the search index in batches to find which blob IDs are already indexed.
    private async Task<HashSet<string>> GetExistingIdsAsync(IEnumerable<BlobItem> blobs, CancellationToken ct)
    {
        var ids      = blobs.Select(b => BlobNameToId(b.Name)).ToList();
        var existing = new HashSet<string>();

        foreach (var batch in ids.Chunk(50))
        {
            var filter  = string.Join(" or ", batch.Select(id => $"id eq '{id}'"));
            var options = new Azure.Search.Documents.SearchOptions { Filter = filter, Size = 1000 };
            options.Select.Add("id");

            var results = await _searchClient.SearchAsync<Azure.Search.Documents.Models.SearchDocument>("*", options, ct);

            await foreach (var result in results.Value.GetResultsAsync())
                existing.Add(result.Document["id"]?.ToString() ?? "");
        }

        return existing;
    }

    private static string? GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static double? GetDouble(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
    }

    private static DateTimeOffset? GetDate(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(el.GetString(), out var d))
            return d;
        return null;
    }
}
