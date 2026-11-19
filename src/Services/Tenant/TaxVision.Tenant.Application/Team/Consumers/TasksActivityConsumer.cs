using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>Acciones de Tasks con actor explícito (un handler por evento).</summary>
public static class TasksActivityConsumer
{
    public static Task Handle(
        TaskCreatedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.CreatedByUserId, ActivityVerbs.TaskCreated, evt.TaskId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        TaskCompletedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.CompletedByUserId, ActivityVerbs.TaskCompleted, evt.TaskId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        TaskReopenedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.ReopenedByUserId, ActivityVerbs.TaskReopened, evt.TaskId, null),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        TaskCancelledIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.CancelledByUserId, ActivityVerbs.TaskCancelled, evt.TaskId, null),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        ClientRequestCreatedIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.RequestedByUserId, ActivityVerbs.ClientRequestCreated, evt.ClientRequestId, evt.CustomerId),
            entries,
            unitOfWork,
            correlation,
            ct
        );
}
