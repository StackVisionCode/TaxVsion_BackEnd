using TaxVision.Tasks.Domain.ClientRequests.Events;
using TaxVision.Tasks.Domain.Tasks.Events;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>
/// Eventos de dominio sin consumidor a propósito. Su aviso de integración, si lo tienen, todavía lo publica a
/// mano el handler del comando; cuando pase a un handler de dominio, sale de esta lista. Sin esto quedarían
/// sin handler en la cola domain-events.
/// </summary>
public static class UnconsumedTaskDomainEventHandler
{
    public static void Handle(TaskDeletedDomainEvent _) { }

    public static void Handle(TaskStartedDomainEvent _) { }

    public static void Handle(TaskUnassignedDomainEvent _) { }

    public static void Handle(TaskDueChangedDomainEvent _) { }

    public static void Handle(TaskPriorityChangedDomainEvent _) { }

    public static void Handle(TaskMovedToWaitingOnClientDomainEvent _) { }

    public static void Handle(TaskUnblockedDomainEvent _) { }

    public static void Handle(TaskAttachmentRejectedDomainEvent _) { }

    public static void Handle(ClientRequestSubmittedDomainEvent _) { }

    public static void Handle(ClientRequestDocumentRejectedDomainEvent _) { }

    public static void Handle(ClientRequestResolvedDomainEvent _) { }
}
