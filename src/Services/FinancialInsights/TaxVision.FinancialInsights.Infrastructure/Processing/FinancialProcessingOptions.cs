namespace TaxVision.FinancialInsights.Infrastructure.Processing;

public sealed class FinancialProcessingOptions
{
    public const string Section = "FinancialProcessing";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 10;

    // Un estado de cuenta real con pdftotext -layout: ~4 000 caracteres por página.
    public int MaxPages { get; set; } = 40;
    public int MaxVisionPages { get; set; } = 3;
    public int MaxTextCharacters { get; set; } = 160_000;
    public int MaxDocumentsPerTenantMonth { get; set; } = 10;
    public string[] AllowedDownloadHosts { get; set; } = ["localhost", "127.0.0.1"];
}

public sealed class OpenAiFinancialOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.4-mini";

    // Por tramo. En modelos que razonan, el razonamiento también gasta de este tope.
    public int MaxOutputTokens { get; set; } = 16_000;

    /// <summary>Vacío = no se manda (modelos sin razonamiento).</summary>
    public string ReasoningEffort { get; set; } = "low";

    /// <summary>El documento se analiza por tramos de páginas para que cada respuesta quepa.</summary>
    public int ChunkPages { get; set; } = 4;
    public int ChunkCharacters { get; set; } = 12_000;
}

public sealed class DocumentProcessingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record ExtractedPage(int Number, string Text, byte[]? ImageBytes = null, string? ImageMediaType = null);
