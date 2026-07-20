using BuildingBlocks.Domain;

namespace TaxVision.Growth.Infrastructure.Persistence.Audit;

/// <summary>
/// Append-only, redacted audit record shared by the Growth host. Platform operations use
/// PlatformTenant.Id rather than an empty tenant identifier.
/// </summary>
public sealed class GrowthAuditEntry : TenantEntity
{
    public string BoundedContext { get; private set; } = default!;
    public string AggregateType { get; private set; } = default!;
    public Guid AggregateId { get; private set; }
    public long? AggregateVersion { get; private set; }
    public string Action { get; private set; } = default!;
    public string ActorId { get; private set; } = default!;
    public string ActorType { get; private set; } = default!;
    public DateTime OccurredAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? CausationId { get; private set; }
    public string? TraceId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? Reason { get; private set; }

    private GrowthAuditEntry() { }

    public static GrowthAuditEntry Create(
        Guid tenantId,
        string boundedContext,
        string aggregateType,
        Guid aggregateId,
        long? aggregateVersion,
        string action,
        string actorId,
        string actorType,
        DateTime occurredAtUtc,
        string? correlationId = null,
        string? causationId = null,
        string? traceId = null,
        string? beforeJson = null,
        string? afterJson = null,
        string? reason = null
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(boundedContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);

        if (aggregateId == Guid.Empty)
            throw new ArgumentException("AggregateId is required.", nameof(aggregateId));
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(occurredAtUtc));

        var entry = new GrowthAuditEntry
        {
            BoundedContext = boundedContext,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            AggregateVersion = aggregateVersion,
            Action = action,
            ActorId = actorId,
            ActorType = actorType,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,
            CausationId = causationId,
            TraceId = traceId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason,
        };
        entry.SetTenant(tenantId);

        return entry;
    }
}
