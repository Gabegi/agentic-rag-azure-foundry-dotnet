namespace InvoiceIndexer.Services;

public interface IEmbeddingService
{
    Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
