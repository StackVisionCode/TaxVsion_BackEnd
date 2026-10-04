namespace TaxVision.Signature.Application.Requests.Public.GetDocument;

/// <summary>
/// F5 — Comando que resuelve el token, valida que la verificación requerida esté completa, baja
/// los bytes del PDF original desde CloudStorage con el M2M de Signature y registra el audit
/// trail <c>DocumentViewed</c>.
/// </summary>
public sealed record GetPublicDocumentCommand(string Token, string? ClientIp, string? UserAgent);

public sealed record PublicDocumentStream(byte[] Content, string ContentType, string FileName);
