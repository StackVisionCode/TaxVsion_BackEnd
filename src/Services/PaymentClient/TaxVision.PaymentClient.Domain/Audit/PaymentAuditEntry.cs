using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.Audit;

/// <summary>Registro inmutable de un cambio en cualquier aggregate de PaymentClient
/// (append-only). No cuelga de otro aggregate: cada entrada es su propia raíz, escrita
/// directamente por <c>IPaymentAuditLogWriter</c>. Nunca se hard-delete (retención
/// indefinida, compliance financiero).</summary>
public sealed class PaymentAuditEntry : BaseEntity
{
    public Guid TenantId { get; private set; }
    public string AggregateType { get; private set; } = default!;
    public Guid AggregateId { get; private set; }
    public PaymentAuditAction Action { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string ActorType { get; private set; } = default!;
    public DateTime OccurredAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }
    public string? BeforePayload { get; private set; }
    public string? AfterPayload { get; private set; }
    public string? Reason { get; private set; }

    private PaymentAuditEntry() { }

    public static Result<PaymentAuditEntry> Create(
        Guid tenantId,
        string aggregateType,
        Guid aggregateId,
        PaymentAuditAction action,
        Guid actorUserId,
        string actorType,
        DateTime occurredAtUtc,
        string? correlationId,
        Guid? causationId,
        string? beforePayload,
        string? afterPayload,
        string? reason
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PaymentAuditEntry>(
                new Error("PaymentAuditEntry.InvalidTenant", "TenantId is required.")
            );

        if (string.IsNullOrWhiteSpace(aggregateType))
            return Result.Failure<PaymentAuditEntry>(
                new Error("PaymentAuditEntry.InvalidAggregateType", "AggregateType is required.")
            );

        if (string.IsNullOrWhiteSpace(actorType))
            return Result.Failure<PaymentAuditEntry>(
                new Error("PaymentAuditEntry.InvalidActorType", "ActorType is required.")
            );

        return Result.Success(
            new PaymentAuditEntry
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
            }
        );
    }
}
