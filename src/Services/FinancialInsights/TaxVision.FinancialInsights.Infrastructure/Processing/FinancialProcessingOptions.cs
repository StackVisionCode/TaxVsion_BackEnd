namespace TaxVision.FinancialInsights.Infrastructure.Processing;

public sealed class FinancialProcessingOptions
{
    public const string Section = "FinancialProcessing";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 10;
    public int MaxPages { get; set; } = 10;
    public int MaxVisionPages { get; set; } = 3;
    public int MaxTextCharacters { get; set; } = 30_000;
    public int MaxDocumentsPerTenantMonth { get; set; } = 10;
    public string[] AllowedDownloadHosts { get; set; } = ["localhost", "127.0.0.1"];
}

public sealed class OpenAiFinancialOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.4-mini";
    public int MaxOutputTokens { get; set; } = 4_000;
}

public sealed class DocumentProcessingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record ExtractedPage(int Number, string Text, byte[]? ImageBytes = null, string? ImageMediaType = null);
