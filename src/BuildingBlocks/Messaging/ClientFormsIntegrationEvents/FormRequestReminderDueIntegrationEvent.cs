namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_reminder_due.v1</summary>
public sealed record FormRequestReminderDueIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required string Title { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public required bool IsOverdue { get; init; }
    public required int ReminderNumber { get; init; }
    public required bool IsManual { get; init; }
    public Guid? SentByUserId { get; init; }
}
