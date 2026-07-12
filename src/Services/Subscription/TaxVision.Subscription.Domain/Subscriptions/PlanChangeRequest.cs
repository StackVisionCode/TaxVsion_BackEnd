using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Settings;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Solicitud de cambio de plan de la suscripción base, aplicada de inmediato o diferida
/// al fin del período actual según <see cref="PlanChangeEffectiveMode"/>. No calcula
/// prorrateo — un cambio diferido simplemente se aplica en la próxima renovación, que
/// cobrará el precio del plan nuevo con normalidad. Entidad hija de
/// <see cref="TenantSubscription"/>: su configuración EF requiere ValueGeneratedNever()
/// (ver guardrail de persistencia).
/// </summary>
public sealed class PlanChangeRequest : BaseEntity
{
    public Guid TenantSubscriptionId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FromPlanId { get; private set; }
    public Guid FromPlanVersionId { get; private set; }
    public string FromPlanCode { get; private set; } = default!;
    public Guid ToPlanId { get; private set; }
    public Guid ToPlanVersionId { get; private set; }
    public string ToPlanCode { get; private set; } = default!;
    public PlanChangeEffectiveMode EffectiveMode { get; private set; }
    public PlanChangeRequestStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime EffectiveAtUtc { get; private set; }
    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    private PlanChangeRequest() { }

    public static Result<PlanChangeRequest> Create(
        Guid tenantSubscriptionId,
        Guid tenantId,
        Guid fromPlanId,
        Guid fromPlanVersionId,
        string fromPlanCode,
        Guid toPlanId,
        Guid toPlanVersionId,
        string toPlanCode,
        PlanChangeEffectiveMode effectiveMode,
        Guid requestedByUserId,
        DateTime effectiveAtUtc,
        DateTime nowUtc)
    {
        if (tenantSubscriptionId == Guid.Empty)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.InvalidSubscription", "TenantSubscriptionId is required."));

        if (tenantId == Guid.Empty)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.InvalidTenant", "TenantId is required."));

        if (fromPlanId == toPlanId && fromPlanVersionId == toPlanVersionId)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.SamePlan", "Target plan is the same as the current plan."));

        return Result.Success(new PlanChangeRequest
        {
            TenantSubscriptionId = tenantSubscriptionId,
            TenantId = tenantId,
            FromPlanId = fromPlanId,
            FromPlanVersionId = fromPlanVersionId,
            FromPlanCode = fromPlanCode,
            ToPlanId = toPlanId,
            ToPlanVersionId = toPlanVersionId,
            ToPlanCode = toPlanCode,
            EffectiveMode = effectiveMode,
            Status = PlanChangeRequestStatus.Pending,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            EffectiveAtUtc = effectiveAtUtc,
        });
    }

    public Result MarkApplied(DateTime nowUtc)
    {
        if (Status != PlanChangeRequestStatus.Pending)
            return Result.Failure(new Error("PlanChangeRequest.InvalidTransition", $"Cannot apply from {Status}."));

        Status = PlanChangeRequestStatus.Applied;
        AppliedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkCancelled(DateTime nowUtc)
    {
        if (Status != PlanChangeRequestStatus.Pending)
            return Result.Failure(new Error("PlanChangeRequest.InvalidTransition", $"Cannot cancel from {Status}."));

        Status = PlanChangeRequestStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        return Result.Success();
    }
}
