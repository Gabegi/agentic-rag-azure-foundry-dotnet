using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using InvoiceIndexer.Configuration;
using InvoiceIndexer.Models;
using Microsoft.Extensions.Logging;

namespace InvoiceIndexer.Services;

public class DocumentService : IDocumentService
{
    private readonly BlobContainerClient _containerClient;
    private readonly DocumentIntelligenceClient _diClient;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IndexerConfig config,
        BlobServiceClient blobServiceClient,
        DocumentIntelligenceClient diClient,
        ILogger<DocumentService> logger)
    {
        _containerClient = blobServiceClient.GetBlobContainerClient(config.StorageContainer);
        _diClient        = diClient;
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

        _logger.LogInformation("Found {Count} PDF blobs", blobs.Count);
        return blobs;
    }

    public async Task<IEnumerable<InvoiceDocument>> ExtractDocumentsAsync(
        IEnumerable<BlobItem> blobs,
        CancellationToken ct = default)
    {
        var documents = new List<InvoiceDocument>();
        var sw        = new System.Diagnostics.Stopwatch();

        foreach (var blob in blobs)
        {
            sw.Restart();

            try
            {
                var blobClient = _containerClient.GetBlobClient(blob.Name);
                var download   = await blobClient.DownloadContentAsync(ct);

                var operation = await _diClient.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-invoice",
                    download.Value.Content,
                    cancellationToken: ct);

                var invoice = operation.Value.Documents?.FirstOrDefault();

                if (invoice == null)
                {
                    _logger.LogWarning("No invoice found in {Name}", blob.Name);
                    continue;
                }

                var vendor       = invoice.Fields.TryGetValue("VendorName",    out var v)  ? v.Content     : null;
                var amount       = invoice.Fields.TryGetValue("InvoiceTotal",  out var a)  ? a.ValueDouble : null;
                var discount     = invoice.Fields.TryGetValue("TotalDiscount", out var d)  ? d.ValueDouble : null;
                var category     = invoice.Fields.TryGetValue("PurchaseOrder", out var c)  ? c.Content     : null;
                var date         = invoice.Fields.TryGetValue("InvoiceDate",   out var dt) ? dt.ValueDate  : null;
                var paymentTerms = invoice.Fields.TryGetValue("PaymentTerm",   out var p)  ? p.Content     : null;

                var content = $"Invoice from {vendor} dated {date:yyyy-MM-dd}. " +
                              $"Category: {category}. Amount: ${amount}. "       +
                              $"Discount: {discount}%. Payment terms: {paymentTerms}.";

                documents.Add(new InvoiceDocument
                {
                    // Azure AI Search keys only allow letters, digits, _ - = — Base64 encode to handle spaces and + in blob names
                    Id           = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(blob.Name)).Replace("+", "-").Replace("/", "_").Replace("=", ""),
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
        }

        _logger.LogInformation("Extracted {Count} documents", documents.Count);
        return documents;
    }
}
