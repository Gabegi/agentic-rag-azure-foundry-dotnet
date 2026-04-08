using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using InvoiceIndexer.Configuration;
using Microsoft.Extensions.Logging;

namespace InvoiceIndexer.Services;

public class PdfExtractorService : IPdfExtractor
{
    private readonly DocumentAnalysisClient _client;
    private readonly ILogger<PdfExtractorService> _logger;

    public PdfExtractorService(DocumentAnalysisClient client, ILogger<PdfExtractorService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(Uri blobUrl, CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting text from {Url}", blobUrl);

        var operation = await _client.AnalyzeDocumentFromUriAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            blobUrl,
            cancellationToken: ct);

        var result = operation.Value;

        if (result.Pages.Count == 0)
        {
            _logger.LogWarning("No pages extracted from {Url}", blobUrl);
            return string.Empty;
        }

        var text = string.Join(" ", result.Pages
            .SelectMany(p => p.Lines)
            .Select(l => l.Content));

        _logger.LogInformation("Extracted {Chars} characters", text.Length);
        return text;
    }
}
