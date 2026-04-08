namespace InvoiceIndexer.Services;

public interface IBlobService
{
    IAsyncEnumerable<(string FileName, Uri BlobUrl)> GetPdfsAsync(CancellationToken ct = default);
}
