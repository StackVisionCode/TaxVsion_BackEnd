using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: sus borradores ABIERTOS (nunca enviados) se reasignan al
/// sucesor, o se descartan si no hay sucesor — un borrador sin dueño ni sucesor es ruido. Lo enviado
/// (Sent) es historial inmutable y no se toca. Threads/emails son tenant+customer (historial): se
/// conservan. Idempotente (al reprocesar ya no quedan borradores abiertos de ese autor).
/// </summary>
public static class CorrespondenceUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IDraftRepository drafts,
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

            foreach (var draft in openDrafts)
            {
                if (successor is { } newAuthor)
                    draft.ReassignAuthor(newAuthor);
                else
                    draft.Discard();
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Offboarded user {UserId}: {Action} {Count} open draft(s) in tenant {TenantId}.",
                msg.UserId,
                successor is null ? "discarded" : "reassigned",
                openDrafts.Count,
                msg.TenantId
            );
        }
    }
}
