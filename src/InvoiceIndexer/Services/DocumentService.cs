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
    private readonly IndexerConfig _config;
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
        _chatClient      = openAiClient.GetChatClient(config.OpenAiGptDeployment);
        _searchClient    = new SearchClient(new Uri(config.SearchEndpoint), config.SearchIndexName, credential);
        _pipeline        = pipeline;
        _config          = config;
        _logger          = logger;
    }

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
                _logger.LogInformation("Extracting {Name}", blob.Name);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // 1. Download blob
                    var blobClient = _containerClient.GetBlobClient(blob.Name);
                    byte[] pdfBytes;
                    await using (var stream = await blobClient.OpenReadAsync(cancellationToken: token))
                    using (var ms = new MemoryStream())
                    {
                        await stream.CopyToAsync(ms, token);
                        pdfBytes = ms.ToArray();
                    }

                    // 2. PdfPig — local text extraction (~50ms, free)
                    string fullText;
                    using (var pdf = PdfDocument.Open(pdfBytes))
                    {
                        fullText = string.Join("\n",
                            pdf.GetPages().Select(p =>
                                string.Join(" ", p.GetWords().Select(w => w.Text))));
                    }

                    // 3. GPT-4o — structured field extraction from raw text
                    var prompt = $"Extract fields from this invoice and return JSON only:\n" +
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
                     $"Invoice text:\n{fullText}";


                    var response = await _pipeline.ExecuteAsync(async t =>
                        await _chatClient.CompleteChatAsync(
                            new ChatMessage[] { new UserChatMessage(prompt) },
                            cancellationToken: t), token);

                    var json = response.Value.Content[0].Text.Trim();

                    // strip markdown code fences if the model wraps the response
                    if (json.StartsWith("```"))
                    {
                        var firstNewline = json.IndexOf('\n');
                        var lastFence    = json.LastIndexOf("```");
                        json = json[(firstNewline + 1)..lastFence].Trim();
                    }

                    using var jsonDoc = JsonDocument.Parse(json);
                    var root = jsonDoc.RootElement;

                    var customer = GetString(root, "customer");
                    var orderId  = GetString(root, "order_id");
                    var category = GetString(root, "category");
                    var shipMode = GetString(root, "ship_mode");
                    var amount   = GetDouble(root, "amount");
                    var discount = GetDouble(root, "discount");
                    var date     = GetDate(root, "date");

                    var content = $"Customer: {customer}\n"    +
                                  $"Date: {date:yyyy-MM-dd}\n" +
                                  $"Amount: ${amount}\n"       +
                                  $"Discount: {discount}%\n"   +
                                  $"Category: {category}\n"    +
                                  $"Order ID: {orderId}\n"     +
                                  $"Ship Mode: {shipMode}\n"   +
                                  $"Full invoice:\n{fullText}";

                    documents.Add(new InvoiceDocument
                    {
                        Id         = BlobNameToId(blob.Name),
                        SourceFile = blob.Name,
                        Customer   = customer,
                        Amount     = amount,
                        Discount   = discount,
                        Category   = category,
                        Date       = date,
                        OrderId    = orderId,
                        ShipMode   = shipMode,
                        Content    = content
                    });

                    _logger.LogInformation("Extracted {Name} in {Ms}ms", blob.Name, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to process {Name}: {Error}", blob.Name, ex.Message);
                }
            });

        _logger.LogInformation("Extracted {Count} documents", documents.Count);
        return documents;
    }

    private static string BlobNameToId(string blobName) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(blobName))
            .Replace("+", "-").Replace("/", "_").Replace("=", "");

    private async Task<HashSet<string>> GetExistingIdsAsync(IEnumerable<BlobItem> blobs, CancellationToken ct)
    {
        var ids      = blobs.Select(b => BlobNameToId(b.Name)).ToList();
        var existing = new HashSet<string>();

        foreach (var batch in ids.Chunk(1000))
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
