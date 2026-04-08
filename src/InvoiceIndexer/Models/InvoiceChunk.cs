using System.Text.Json.Serialization;

namespace InvoiceIndexer.Models;

public class InvoiceChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("page_number")]
    public int PageNumber { get; set; }

    [JsonPropertyName("page_chunk")]
    public string PageChunk { get; set; } = default!;

    [JsonPropertyName("page_embedding_text_3_large")]
    public ReadOnlyMemory<float> PageEmbeddingText3Large { get; set; }
}
