namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Respuesta HTTP de <c>GET /correspondence/messages/{id}/body</c>. Vive solo por la duración de
/// la request — Correspondence nunca lo persiste (plan de diseño §17).
/// </summary>
public sealed record MessageBodyResult(string? HtmlBody, string? TextBody, IReadOnlyDictionary<string, string> Headers);
