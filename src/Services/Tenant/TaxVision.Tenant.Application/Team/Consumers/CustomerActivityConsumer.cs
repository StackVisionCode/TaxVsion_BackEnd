using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Alta de clientes y asignación de preparador (el actor es quien asigna, no el asignado).</summary>
public static class CustomerActivityConsumer
{
    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.CreatedByUserId, ActivityVerbs.CustomerCreated, evt.CustomerId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        CustomerPreparerAssignedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.AssignedByUserId, ActivityVerbs.CustomerPreparerAssigned, evt.CustomerId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );
}
