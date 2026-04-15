namespace InvoiceIndexer.Models;

public class InvoiceDocument
{
    public string Id { get; set; }
    public string Vendor { get; set; }
    public double? Amount { get; set; }
    public double? Discount { get; set; }
    public string Category { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string PaymentTerms { get; set; }
    public string SourceFile { get; set; }
    public string Content { get; set; }
    public float[] ContentVector { get; set; }
}
