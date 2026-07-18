namespace TaxVision.Correspondence.Application.Compose;

/// <summary>Fila de destinatario para <see cref="DraftDetail"/> (Fase 11) — <c>Type</c> serializado como string (<c>To</c>/<c>Cc</c>/<c>Bcc</c>), mismo criterio que <c>DownloadStatus</c> en <see cref="Messages.AttachmentSummary"/>.</summary>
public sealed record DraftRecipientSummary(string Address, string Type, string? DisplayName);
