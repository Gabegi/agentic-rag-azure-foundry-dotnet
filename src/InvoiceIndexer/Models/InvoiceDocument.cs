using System.Text.Json.Serialization;

namespace InvoiceIndexer.Models;

public class InvoiceDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; }

    [JsonPropertyName("amount")]
    public double? Amount { get; set; }

    [JsonPropertyName("discount")]
    public double? Discount { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; }

    [JsonPropertyName("date")]
    public DateTimeOffset? Date { get; set; }

    [JsonPropertyName("payment_terms")]
    public string PaymentTerms { get; set; }

    [JsonPropertyName("source_file")]
    public string SourceFile { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }

    [JsonPropertyName("content_vector")]
    public float[] ContentVector { get; set; }
}
