namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Body completo de un mensaje (Fase 8, body fetch bajo demanda) — a diferencia de <see cref="RawMessage"/>
/// (metadata-only, usado por el pipeline de webhooks) esto SÍ incluye el contenido del correo, pero
/// nunca bytes de attachments (esos se piden aparte, Fase 9).
/// </summary>
public sealed record MessageBody(
    long MimeSizeBytes,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<RawMessageAttachment> Attachments
);
