using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Downgrade agendado para el fin del período actual. NUNCA cobra, NUNCA prorratea, NUNCA
/// genera crédito ni reembolso — el tenant sigue disfrutando el plan actual hasta la
/// renovación, momento en el que <see cref="TenantSubscription.ApplyPendingDowngrade"/>
/// (llamado por el job de renovación, antes de facturar) cambia el plan; la renovación normal
/// cobra entonces el precio del plan nuevo. Entidad hija de <see cref="TenantSubscription"/>:
/// su configuración EF requiere ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class PendingDowngrade : BaseEntity
{
    public Guid TenantSubscriptionId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FromPlanId { get; private set; }
    public Guid FromPlanVersionId { get; private set; }
    public string FromPlanCode { get; private set; } = default!;
    public Guid ToPlanId { get; private set; }
    public Guid ToPlanVersionId { get; private set; }
    public string ToPlanCode { get; private set; } = default!;
    /// <summary>Null = mantener el ciclo de facturación actual, no se pidió cambiarlo.</summary>
    public BillingCycle? ToBillingCycle { get; private set; }
    public PendingDowngradeStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    /// <summary>Fin del período vigente al momento de la solicitud — cuándo se aplicará.</summary>
    public DateTime EffectiveAtUtc { get; private set; }
    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    private PendingDowngrade() { }

    public static Result<PendingDowngrade> Create(
        Guid tenantSubscriptionId,
        Guid tenantId,
        Guid fromPlanId,
        Guid fromPlanVersionId,
        string fromPlanCode,
        Guid toPlanId,
        Guid toPlanVersionId,
        string toPlanCode,
        BillingCycle? toBillingCycle,
        Guid requestedByUserId,
        DateTime effectiveAtUtc,
        DateTime nowUtc)
    {
        if (tenantSubscriptionId == Guid.Empty)
            return Result.Failure<PendingDowngrade>(new Error("PendingDowngrade.InvalidSubscription", "TenantSubscriptionId is required."));

        if (tenantId == Guid.Empty)
            return Result.Failure<PendingDowngrade>(new Error("PendingDowngrade.InvalidTenant", "TenantId is required."));

        if (fromPlanId == toPlanId && fromPlanVersionId == toPlanVersionId && toBillingCycle is null)
            return Result.Failure<PendingDowngrade>(new Error("PendingDowngrade.SamePlan", "Target plan is the same as the current plan."));

        return Result.Success(new PendingDowngrade
        {
            TenantSubscriptionId = tenantSubscriptionId,
            TenantId = tenantId,
            FromPlanId = fromPlanId,
            FromPlanVersionId = fromPlanVersionId,
            FromPlanCode = fromPlanCode,
            ToPlanId = toPlanId,
            ToPlanVersionId = toPlanVersionId,
            ToPlanCode = toPlanCode,
            ToBillingCycle = toBillingCycle,
            Status = PendingDowngradeStatus.Scheduled,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            EffectiveAtUtc = effectiveAtUtc,
        });
    }

    public Result MarkApplied(DateTime nowUtc)
    {
        if (Status != PendingDowngradeStatus.Scheduled)
            return Result.Failure(new Error("PendingDowngrade.InvalidTransition", $"Cannot apply from {Status}."));

        Status = PendingDowngradeStatus.Applied;
        AppliedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkCancelled(DateTime nowUtc)
    {
        if (Status != PendingDowngradeStatus.Scheduled)
            return Result.Failure(new Error("PendingDowngrade.InvalidTransition", $"Cannot cancel from {Status}."));

        Status = PendingDowngradeStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        return Result.Success();
    }
}
