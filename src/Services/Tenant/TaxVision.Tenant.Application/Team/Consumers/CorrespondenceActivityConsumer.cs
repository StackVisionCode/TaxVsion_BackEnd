using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Correo enviado desde Correspondence: respuesta en un hilo, o correo nuevo (aún sin hilo).</summary>
public static class CorrespondenceActivityConsumer
{
    public static Task Handle(
        CorrespondenceEmailSentIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            evt.ThreadId is { } threadId
                ? new ActivityFact(evt.SentByUserId, ActivityVerbs.EmailReplied, threadId, evt.CustomerId)
                : new ActivityFact(evt.SentByUserId, ActivityVerbs.EmailSent, evt.DraftId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );
}
