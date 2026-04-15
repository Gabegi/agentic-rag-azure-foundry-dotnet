using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;
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
        TokenCredential credential,
        DocumentIntelligenceClient diClient,
        ILogger<DocumentService> logger)
    {
        _containerClient = new BlobServiceClient(new Uri(config.StorageAccountUrl), credential)
            .GetBlobContainerClient(config.StorageContainer);
        _diClient = diClient;
        _logger   = logger;
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

            _logger.LogInformation("Found blob {Name}", blob.Name);
            blobs.Add(blob);
        }

        _logger.LogInformation("Found {Count} PDF blobs", blobs.Count);
        return blobs;
    }

    public async Task<IEnumerable<InvoiceDocument>> ExtractDocumentsAsync(
        IEnumerable<BlobItem> blobs,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting {Count} documents", blobs.Count());

        var documents = new List<InvoiceDocument>();

        foreach (var blob in blobs)
        {
            _logger.LogInformation("Extracting {Name}", blob.Name);

            var blobClient = _containerClient.GetBlobClient(blob.Name);

            var operation = await _diClient.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-invoice",
                new AnalyzeDocumentContent { UrlSource = blobClient.Uri },
                cancellationToken: ct);

            var invoice = operation.Value.Documents?.FirstOrDefault();

            if (invoice == null)
            {
                _logger.LogWarning("No invoice found in {Name}", blob.Name);
                continue;
            }

            var vendor       = invoice.Fields.TryGetValue("VendorName",    out var v)  ? v.Content     : null;
            var amount       = invoice.Fields.TryGetValue("InvoiceTotal",  out var a)  ? a.ValueNumber : null;
            var discount     = invoice.Fields.TryGetValue("TotalDiscount", out var d)  ? d.ValueNumber : null;
            var category     = invoice.Fields.TryGetValue("PurchaseOrder", out var c)  ? c.Content     : null;
            var date         = invoice.Fields.TryGetValue("InvoiceDate",   out var dt) ? dt.ValueDate  : null;
            var paymentTerms = invoice.Fields.TryGetValue("PaymentTerm",   out var p)  ? p.Content     : null;

            var content = $"Invoice from {vendor} dated {date:yyyy-MM-dd}. " +
                          $"Category: {category}. Amount: ${amount}. "       +
                          $"Discount: {discount}%. Payment terms: {paymentTerms}.";

            documents.Add(new InvoiceDocument
            {
                Id           = blob.Name.Replace(".pdf", "").Replace("/", "-"),
                SourceFile   = blob.Name,
                Vendor       = vendor,
                Amount       = amount,
                Discount     = discount,
                Category     = category,
                Date         = date,
                PaymentTerms = paymentTerms,
                Content      = content
            });

            _logger.LogInformation("Extracted invoice from {Name}", blob.Name);
        }

        _logger.LogInformation("Extracted {Count} documents", documents.Count);
        return documents;
    }

    public Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task UploadDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default)
        => throw new NotImplementedException();
}
