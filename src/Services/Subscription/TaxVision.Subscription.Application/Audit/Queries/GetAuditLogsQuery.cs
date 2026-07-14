namespace TaxVision.Subscription.Application.Audit.Queries;

public sealed record GetAuditLogsQuery(
    Guid TenantId, string? AggregateType, Guid? AggregateId, DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize);
