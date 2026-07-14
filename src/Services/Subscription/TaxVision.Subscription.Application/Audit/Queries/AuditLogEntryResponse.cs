namespace TaxVision.Subscription.Application.Audit.Queries;

public sealed record AuditLogEntryResponse(
    Guid Id,
    string AggregateType,
    Guid AggregateId,
    string Action,
    Guid ActorUserId,
    string ActorType,
    DateTime OccurredAtUtc,
    string? CorrelationId,
    string? BeforePayload,
    string? AfterPayload,
    string? Reason
);
