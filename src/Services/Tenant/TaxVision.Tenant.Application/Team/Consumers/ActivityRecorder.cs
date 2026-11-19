using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>
/// Paso común de los consumers del historial: correlación primero, nada sin actor explícito e idempotente por
/// <c>EventId</c>. Dos entregas simultáneas chocan en el índice único; el reintento ya la encuentra y no inserta.
/// </summary>
public static class ActivityRecorder
{
    public static async Task RecordAsync(
        IIntegrationEvent evt,
        ActivityFact fact,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            if (fact.ActorUserId is not { } actor || actor == Guid.Empty)
                return;
            if (await entries.ExistsAsync(evt.TenantId, evt.EventId, ct))
                return;

            entries.Add(Entry(evt, fact, actor));
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // Un evento sin tenant o sin id es un contrato roto: falla para que quede en la DLQ, no se descarta callado.
    private static ActivityEntry Entry(IIntegrationEvent evt, ActivityFact fact, Guid actor)
    {
        var created = ActivityEntry.Create(
            new ActivityEntryDraft(
                evt.TenantId,
                evt.EventId,
                actor,
                fact.Verb,
                fact.ResourceId,
                fact.CustomerId,
                fact.Visibility,
                evt.OccurredOn
            )
        );
        return created.IsSuccess
            ? created.Value
            : throw new InvalidOperationException(
                $"Activity entry rejected for event {evt.EventId}: {created.Error.Code}."
            );
    }

    private static string CorrelationOf(IIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
