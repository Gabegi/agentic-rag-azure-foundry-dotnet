namespace InvoiceIndexer.Services;

public interface IBlobService
{
    IAsyncEnumerable<(string FileName, Stream Content)> GetPdfsAsync(CancellationToken ct = default);
}
