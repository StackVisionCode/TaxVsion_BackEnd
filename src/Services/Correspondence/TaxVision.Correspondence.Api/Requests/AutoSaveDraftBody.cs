namespace TaxVision.Correspondence.Api.Requests;

/// <summary>
/// <c>PATCH /correspondence/drafts/{id}</c> (Fase 11) — cuerpo parcial, plan §22: cada campo es
/// independientemente opcional, un campo ausente/null nunca pisa lo ya guardado (ver
/// <c>AutoSaveDraftHandler</c> para el detalle de cómo se reconcilian To/Cc/Bcc).
/// </summary>
public sealed record AutoSaveDraftBody(
    string? Subject,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyList<DraftRecipientBody>? To,
    IReadOnlyList<DraftRecipientBody>? Cc,
    IReadOnlyList<DraftRecipientBody>? Bcc
);
