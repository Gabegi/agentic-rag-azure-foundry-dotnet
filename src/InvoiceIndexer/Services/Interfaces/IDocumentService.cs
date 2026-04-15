using Azure.Storage.Blobs.Models;
using InvoiceIndexer.Models;

public interface IDocumentService
{
    Task<IEnumerable<BlobItem>> ReadBlobsAsync(CancellationToken ct = default);
    Task<IEnumerable<InvoiceDocument>> ExtractDocumentsAsync(IEnumerable<BlobItem> blobs, CancellationToken ct = default);
    Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default);
    Task UploadDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default);
}
