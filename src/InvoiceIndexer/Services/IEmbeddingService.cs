using InvoiceIndexer.Models;

namespace InvoiceIndexer.Services;

public interface IEmbeddingService
{
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default);
    Task UploadDocumentsAsync(IEnumerable<InvoiceDocument> documents, CancellationToken ct = default);
}
