namespace InvoiceIndexer.Services;

public interface IPdfExtractor
{
    Task<string> ExtractTextAsync(Uri blobUrl, CancellationToken ct = default);
}
