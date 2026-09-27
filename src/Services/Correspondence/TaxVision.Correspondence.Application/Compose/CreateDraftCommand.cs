namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fase 11 — <c>POST /correspondence/drafts</c>: correspondencia nueva desde cero (a diferencia
/// de <see cref="StartReplyCommand"/>, que arranca desde un <see cref="Domain.Inbox.IncomingEmail"/>
/// existente).
/// </summary>
/// <param name="VisibleAccountIds">
/// Buzones que el caller puede usar para redactar. <c>null</c> = todos los de la oficina. Ver
/// <see cref="SendingAccountGuard"/>.
/// </param>
public sealed record CreateDraftCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid AccountId,
    Guid ActorId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
