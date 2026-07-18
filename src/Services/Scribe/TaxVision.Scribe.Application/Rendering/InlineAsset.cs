namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Referencia (no bytes) a un asset que va embebido inline en el correo vía Content-ID. Mismo
/// shape que el InlineAsset de Postmaster (§14.6) — Postmaster descarga los bytes reales al armar
/// el MIME multipart/related; Scribe nunca toca CloudStorage para esto.
/// </summary>
public sealed record InlineAsset(string ContentId, Guid CloudStorageFileId, string ContentType, long SizeBytes);
