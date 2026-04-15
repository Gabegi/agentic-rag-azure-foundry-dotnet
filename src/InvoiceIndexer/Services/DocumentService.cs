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
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(IndexerConfig config, TokenCredential credential, ILogger<DocumentService> logger)
    {
        _containerClient = new BlobServiceClient(new Uri(config.StorageAccountUrl), credential)
            .GetBlobContainerClient(config.StorageContainer);
        _logger = logger;
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

    public Task<IEnumerable<InvoiceDocument>> ExtractDocumentsAsync(IEnumerable<BlobItem> blobs, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task UploadDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default)
        => throw new NotImplementedException();
}
