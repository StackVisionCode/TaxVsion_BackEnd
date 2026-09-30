namespace TaxVision.Correspondence.Application.Compose;

/// <param name="RequestingUserId">
/// Quién pregunta. A1 — un borrador es un correo a medio escribir: solo lo lee su autor, o quien ve el
/// buzón de la oficina (<paramref name="CanReadOtherUsersDrafts"/>).
/// </param>
public sealed record GetDraftQuery(
    Guid TenantId,
    Guid DraftId,
    Guid RequestingUserId = default,
    bool CanReadOtherUsersDrafts = true
);
