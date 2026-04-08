using Azure.Storage.Blobs;
using InvoiceIndexer.Configuration;
using Microsoft.Extensions.Logging;

namespace InvoiceIndexer.Services;

public class BlobService : IBlobService
{
    private readonly BlobContainerClient _blobContainerClient;
    private readonly ILogger<BlobService> _logger;

    public BlobService(BlobServiceClient blobServiceClient, IndexerConfig config, ILogger<BlobService> logger)
    {
        _blobContainerClient = blobServiceClient.GetBlobContainerClient(config.StorageContainer);
        _logger              = logger;
    }

    public async IAsyncEnumerable<(string FileName, Uri BlobUrl)> GetPdfsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var blob in _blobContainerClient.GetBlobsAsync(cancellationToken: ct))
        {
            if (!blob.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping non-PDF {Name}", blob.Name);
                continue;
            }

            var blobUrl = _blobContainerClient.GetBlobClient(blob.Name).Uri;
            _logger.LogInformation("Found {Name} at {Url}", blob.Name, blobUrl);

            yield return (blob.Name, blobUrl);
        }
    }
}
