using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Audit;

/// <summary>
/// Registro inmutable de un cambio en cualquier aggregate del servicio (append-only — no
/// tiene métodos de mutación más allá de su factory). No cuelga de otro aggregate: cada
/// entrada es su propia raíz, escrita directamente por <c>ISubscriptionAuditLogWriter</c>.
/// </summary>
public sealed class SubscriptionAuditLog : BaseEntity
{
    public Guid TenantId { get; private set; }
    public string AggregateType { get; private set; } = default!;
    public Guid AggregateId { get; private set; }
    public string Action { get; private set; } = default!;
    public Guid ActorUserId { get; private set; }
    public string ActorType { get; private set; } = default!;
    public DateTime OccurredAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }
    public string? BeforePayload { get; private set; }
    public string? AfterPayload { get; private set; }
    public string? Reason { get; private set; }

    private SubscriptionAuditLog() { }

    public static Result<SubscriptionAuditLog> Create(
        Guid tenantId,
        string aggregateType,
        Guid aggregateId,
        string action,
        Guid actorUserId,
        string actorType,
        DateTime occurredAtUtc,
        string? correlationId,
        Guid? causationId,
        string? beforePayload,
        string? afterPayload,
        string? reason)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SubscriptionAuditLog>(new Error("AuditLog.InvalidTenant", "TenantId is required."));

        if (string.IsNullOrWhiteSpace(aggregateType))
            return Result.Failure<SubscriptionAuditLog>(new Error("AuditLog.InvalidAggregateType", "AggregateType is required."));

        if (string.IsNullOrWhiteSpace(action))
            return Result.Failure<SubscriptionAuditLog>(new Error("AuditLog.InvalidAction", "Action is required."));

        return Result.Success(new SubscriptionAuditLog
        {
            TenantId = tenantId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Action = action,
            ActorUserId = actorUserId,
            ActorType = actorType,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,
            CausationId = causationId,
            BeforePayload = beforePayload,
            AfterPayload = afterPayload,
            Reason = reason?.Length > 500 ? reason[..500] : reason,
        });
    }
}
