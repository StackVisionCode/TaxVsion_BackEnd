namespace TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageWithEvents;

public sealed record GetSentMessageWithEventsQuery(Guid TenantId, Guid SentMessageId);

public sealed record SentMessageEventDto(
    Guid Id,
    string EventType,
    DateTime EventAtUtc,
    Guid? RecipientId,
    string? Reason
);

public sealed record SentMessageWithEventsDto(
    Guid Id,
    string Status,
    DateTime QueuedAtUtc,
    DateTime? SentAtUtc,
    IReadOnlyList<SentMessageEventDto> Events
);
