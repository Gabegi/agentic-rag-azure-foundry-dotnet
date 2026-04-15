using InvoiceIndexer.Models;

public interface ISearchService
{
    Task EnsureIndexAsync();
    Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync();
    Task UploadChunksAsync(IEnumerable<InvoiceDocument> documents);
}
