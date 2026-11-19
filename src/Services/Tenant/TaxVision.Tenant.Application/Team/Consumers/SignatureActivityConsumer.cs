using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Solicitudes de firma. En el envío solo cuenta <c>SentByUserId</c>: el creador no tiene por qué ser quien envía.</summary>
public static class SignatureActivityConsumer
{
    public static Task Handle(
        SignatureRequestCreatedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.CreatedByUserId, ActivityVerbs.SignatureCreated, evt.SignatureRequestId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        SignatureRequestSentIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.SentByUserId, ActivityVerbs.SignatureSent, evt.SignatureRequestId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        PreparerSignedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.PreparerUserId, ActivityVerbs.SignaturePreparerSigned, evt.SignatureRequestId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        SignatureRequestCanceledIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.CanceledByUserId, ActivityVerbs.SignatureCanceled, evt.SignatureRequestId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );
}
