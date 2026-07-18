namespace TaxVision.Correspondence.Application.Compose;

/// <summary>Fila de adjunto para <see cref="DraftDetail"/> (Fase 11) — mirror 1:1 de <see cref="Domain.Compose.DraftAttachmentRef"/>, sin agregar nada (los adjuntos en sí son Fase 12, este DTO solo lee lo que ya exista).</summary>
public sealed record DraftAttachmentSummary(Guid FileId, string Filename, string ContentType, long SizeBytes);
