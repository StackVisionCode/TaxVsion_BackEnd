namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fase 11 — <c>POST /correspondence/drafts</c>: correspondencia nueva desde cero (a diferencia
/// de <see cref="StartReplyCommand"/>, que arranca desde un <see cref="Domain.Inbox.IncomingEmail"/>
/// existente).
/// </summary>
public sealed record CreateDraftCommand(Guid TenantId, Guid CustomerId, Guid AccountId, Guid ActorId);
