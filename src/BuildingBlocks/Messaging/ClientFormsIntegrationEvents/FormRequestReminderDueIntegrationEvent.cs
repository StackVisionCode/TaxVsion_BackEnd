using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_reminder_due.v1</summary>
[MessageIdentity("BuildingBlocks.Messaging.ClientFormsIntegrationEvents.FormRequestReminderDueIntegrationEvent")]
public sealed record FormRequestReminderDueIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required string Title { get; init; }
    public DateTime? DueAtUtc { get; init; }

    /// <summary>Fecha civil del vencimiento (aditivo): el instante es el fin de ese día en la oficina.</summary>
    public DateOnly? DueOn { get; init; }
    public required bool IsOverdue { get; init; }
    public required int ReminderNumber { get; init; }
    public required bool IsManual { get; init; }
    public Guid? SentByUserId { get; init; }
}
