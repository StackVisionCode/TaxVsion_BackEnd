namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// <c>PATCH /correspondence/drafts/{id}</c> (Fase 11) — autoguardado parcial, pensado para ser
/// disparado cada pocos segundos (debounced) desde el frontend mientras el usuario escribe.
/// <see cref="Subject"/>/<see cref="HtmlBody"/>/<see cref="TextBody"/> siguen la semántica PATCH de
/// <see cref="Domain.Compose.Draft.AutoSave"/> uno a uno (null = no tocar). <see cref="To"/>/
/// <see cref="Cc"/>/<see cref="Bcc"/> son PATCH por separado a nivel de tipo de destinatario — cada
/// uno null = no tocar ESE tipo, no-null (incluida lista vacía) = reemplazar ESE tipo — ver
/// <see cref="AutoSaveDraftHandler"/> para cómo se reconcilian con
/// <see cref="Domain.Compose.Draft.AutoSave"/>, que solo acepta una única lista combinada.
/// </summary>
public sealed record AutoSaveDraftCommand(
    Guid TenantId,
    Guid DraftId,
    string? Subject,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyList<AutoSaveDraftRecipientInput>? To,
    IReadOnlyList<AutoSaveDraftRecipientInput>? Cc,
    IReadOnlyList<AutoSaveDraftRecipientInput>? Bcc
);
