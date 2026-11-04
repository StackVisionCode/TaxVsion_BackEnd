using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: sus borradores ABIERTOS (nunca enviados) se reasignan al
/// sucesor, o se descartan si no hay sucesor — un borrador sin dueño ni sucesor es ruido. Si el borrador
/// salía de un buzón PERSONAL del retirado (Connectors lo desconecta y purga), pasa al buzón de oficina
/// por defecto (el más antiguo); sin buzón de oficina se descarta, porque el sucesor heredaría un
/// borrador inenviable. Lo enviado (Sent) es historial inmutable y no se toca. Idempotente (al
/// reprocesar ya no quedan borradores abiertos de ese autor). Fail-closed: si Connectors no responde se
/// lanza y Wolverine reintenta, sin reasignar a medias.
/// </summary>
public static class CorrespondenceUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IDraftRepository drafts,
        IConnectorsClient connectors,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<Draft> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
            )
        )
        {
            var openDrafts = await drafts.ListOpenByAuthorAsync(msg.TenantId, msg.UserId, ct);
            if (openDrafts.Count == 0)
                return;

            // Solo un sucesor real (existente y distinto del que se va) recibe los borradores; si no, se descartan.
            var successor = msg.SuccessorUserId is { } candidate && candidate != msg.UserId ? candidate : (Guid?)null;
            int reassigned = 0,
                moved = 0,
                discarded = 0;

            if (successor is not { } newAuthor)
            {
                foreach (var draft in openDrafts)
                    if (draft.Discard().IsSuccess)
                        discarded++;
            }
            else
            {
                var mailboxes = await MailboxesOfLeaverAsync(msg, connectors, ct);
                foreach (var draft in openDrafts)
                {
                    if (mailboxes.Personal.Contains(draft.AccountId))
                    {
                        if (mailboxes.DefaultOffice is not { } office)
                        {
                            if (draft.Discard().IsSuccess)
                                discarded++;
                            continue;
                        }
                        if (draft.MoveToAccount(office).IsSuccess)
                            moved++;
                    }
                    if (draft.ReassignAuthor(newAuthor).IsSuccess)
                        reassigned++;
                }
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Offboarded user {UserId}: reassigned {Reassigned} open draft(s) ({Moved} moved to the office mailbox) and discarded {Discarded} in tenant {TenantId}.",
                msg.UserId,
                reassigned,
                moved,
                discarded,
                msg.TenantId
            );
        }
    }

    private sealed record LeaverMailboxes(IReadOnlySet<Guid> Personal, Guid? DefaultOffice);

    // Correspondence no tiene directorio de buzones: usa el M2M de visibilidad que ya existe. Connectors
    // devuelve las filas aunque ya estén desconectadas, así que no depende del orden entre consumers.
    private static async Task<LeaverMailboxes> MailboxesOfLeaverAsync(
        UserOffboardedIntegrationEvent msg,
        IConnectorsClient connectors,
        CancellationToken ct
    )
    {
        var personal = await connectors.GetVisibleAccountIdsAsync(msg.TenantId, msg.UserId, includeOffice: false, ct);
        if (personal.IsFailure)
            throw new InvalidOperationException($"Connectors mailboxes unavailable: {personal.Error.Code}.");

        var personalIds = personal.Value.ToHashSet();
        if (personalIds.Count == 0)
            return new LeaverMailboxes(personalIds, null);

        var withOffice = await connectors.GetVisibleAccountIdsAsync(msg.TenantId, msg.UserId, includeOffice: true, ct);
        if (withOffice.IsFailure)
            throw new InvalidOperationException($"Connectors mailboxes unavailable: {withOffice.Error.Code}.");

        // Connectors las devuelve por antigüedad: la primera de oficina es la de por defecto.
        var office = withOffice.Value.Where(id => !personalIds.Contains(id)).Select(id => (Guid?)id).FirstOrDefault();
        return new LeaverMailboxes(personalIds, office);
    }
}
