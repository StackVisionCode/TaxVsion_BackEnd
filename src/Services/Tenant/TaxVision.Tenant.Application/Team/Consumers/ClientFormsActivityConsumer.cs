using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Form requests: asignar, completar (revisión del staff) y devolver al cliente.</summary>
public static class ClientFormsActivityConsumer
{
    public static Task Handle(
        FormRequestAssignedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(
                evt.RequestedByUserId,
                ActivityVerbs.FormRequestAssigned,
                evt.FormRequestId,
                CustomerOf(evt.Participant)
            ),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        FormRequestCompletedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(
                evt.CompletedByUserId,
                ActivityVerbs.FormRequestCompleted,
                evt.FormRequestId,
                CustomerOf(evt.Participant)
            ),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        FormRequestReturnedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(
                evt.ReturnedByUserId,
                ActivityVerbs.FormRequestReturned,
                evt.FormRequestId,
                CustomerOf(evt.Participant)
            ),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    // Un prospecto o externo no es cliente: la entrada queda sin cliente.
    private static Guid? CustomerOf(ClientFormParticipant participant) => participant.CustomerId;
}
