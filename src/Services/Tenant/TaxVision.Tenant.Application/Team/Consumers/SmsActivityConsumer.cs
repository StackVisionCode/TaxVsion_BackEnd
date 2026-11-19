using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>SMS enviado por un miembro a un cliente (sin texto ni teléfono).</summary>
public static class SmsActivityConsumer
{
    public static Task Handle(
        SmsMessageSentIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.SentByUserId, ActivityVerbs.SmsSent, evt.MessageId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );
}
