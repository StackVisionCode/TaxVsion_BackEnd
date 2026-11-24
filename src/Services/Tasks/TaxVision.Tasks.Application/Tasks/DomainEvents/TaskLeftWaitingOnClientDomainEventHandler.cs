using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Domain.Tasks.Events;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>Salió de Waiting on client: se cierra el aviso de respuesta del cliente.</summary>
public static class TaskLeftWaitingOnClientDomainEventHandler
{
    public static ReminderTargetClosedIntegrationEvent Handle(
        TaskLeftWaitingOnClientDomainEvent left,
        ICorrelationContext correlation
    ) =>
        TaskReminderContracts.ClientResponseClosed(
            left.TenantId,
            left.TaskId,
            TaskReminderContracts.ClosureReasons.LeftWaiting,
            correlation.CorrelationId
        );
}
