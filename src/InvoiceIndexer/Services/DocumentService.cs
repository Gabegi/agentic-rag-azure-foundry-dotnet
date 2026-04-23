using System.Collections.Concurrent;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Core;
using InvoiceIndexer.Configuration;
using InvoiceIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Resilience;
using Polly;

namespace InvoiceIndexer.Services;

public class DocumentService : IDocumentService
{
    private readonly BlobContainerClient _containerClient;
    private readonly DocumentIntelligenceClient _diClient;
    private readonly SearchClient _searchClient;
    private readonly ResiliencePipeline _pipeline;
    private readonly IndexerConfig _config;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IndexerConfig config,
        BlobServiceClient blobServiceClient,
        DocumentIntelligenceClient diClient,
        TokenCredential credential,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<DocumentService> logger)
    {
        _containerClient = blobServiceClient.GetBlobContainerClient(config.StorageContainer);
        _diClient        = diClient;
        _searchClient    = new SearchClient(new Uri(config.SearchEndpoint), config.SearchIndexName, credential);
        _pipeline        = pipelineProvider.GetPipeline("document-intelligence");
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
        // Document Intelligence free tier allows 500 transactions total
        var newBlobs    = blobs.Where(b => !existingIds.Contains(BlobNameToId(b.Name))).Take(500).ToList();

        _logger.LogInformation("Found {Total} PDF blobs — {New} new (capped at 500), {Skipped} already indexed",
            blobs.Count, newBlobs.Count, blobs.Count - newBlobs.Count);

        return newBlobs;
    }

    public async Task<IEnumerable<InvoiceDocument>> ExtractDocumentsAsync(
        IEnumerable<BlobItem> blobs,
        CancellationToken ct = default)
    {
        var documents = new ConcurrentBag<InvoiceDocument>();

        await Parallel.ForEachAsync(blobs,
            new ParallelOptions { MaxDegreeOfParallelism = _config.DocumentIntelligenceParallelism, CancellationToken = ct },
            async (blob, token) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var blobClient = _containerClient.GetBlobClient(blob.Name);

                    // Stream the blob rather than loading it fully into memory — avoids spikes with concurrent large PDFs
                    var stream = await blobClient.OpenReadAsync(cancellationToken: token);

                    var operation = await _pipeline.ExecuteAsync(async t =>
                        await _diClient.AnalyzeDocumentAsync(
                            WaitUntil.Completed,
                            "prebuilt-invoice",
                            stream,
                            cancellationToken: t),
                        token);

                    var invoice = operation.Value.Documents?.FirstOrDefault();

                    if (invoice == null)
                    {
                        _logger.LogWarning("No invoice found in {Name}", blob.Name);
                        return;
                    }

                    var vendor       = invoice.Fields.TryGetValue("VendorName",    out var v)  ? v.Content     : null;
                    var amount       = invoice.Fields.TryGetValue("InvoiceTotal",  out var a)  ? a.ValueDouble : null;
                    var discount     = invoice.Fields.TryGetValue("TotalDiscount", out var d)  ? d.ValueDouble : null;
                    var category     = invoice.Fields.TryGetValue("PurchaseOrder", out var c)  ? c.Content     : null;
                    // date is DateTimeOffset? — null renders as "" in the content string, which is acceptable
                    var date         = invoice.Fields.TryGetValue("InvoiceDate",   out var dt) ? dt.ValueDate  : null;
                    var paymentTerms = invoice.Fields.TryGetValue("PaymentTerm",   out var p)  ? p.Content     : null;

                    var content = $"Invoice from {vendor} dated {date:yyyy-MM-dd}. " +
                                  $"Category: {category}. Amount: ${amount}. "       +
                                  $"Discount: {discount}%. Payment terms: {paymentTerms}.";

                    documents.Add(new InvoiceDocument
                    {
                        // Azure AI Search keys only allow letters, digits, _ - = — Base64 encode to handle spaces and + in blob names
                        Id           = BlobNameToId(blob.Name),
                        SourceFile   = blob.Name,
                        Vendor       = vendor,
                        Amount       = amount,
                        Discount     = discount,
                        Category     = category,
                        Date         = date,
                        PaymentTerms = paymentTerms,
                        Content      = content
                    });

                    _logger.LogInformation("Processed {Name} in {Ms}ms", blob.Name, sw.ElapsedMilliseconds);
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

        // Fetch in batches of 1000 (Search $filter length limit)
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
}
