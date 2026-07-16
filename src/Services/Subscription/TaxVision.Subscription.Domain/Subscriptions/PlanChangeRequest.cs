using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Solicitud de upgrade de la suscripción base. SIEMPRE cobra el precio COMPLETO del plan
/// destino — sin prorrateo, sin diferencia de precio, sin crédito. Queda en
/// <see cref="PlanChangeRequestStatus.AwaitingPayment"/> hasta que PaymentApp confirme el
/// cobro — <see cref="TenantSubscription.ChangePlan"/> no se llama hasta ese momento (y
/// cuando se llama, también reinicia el ciclo de facturación desde cero, ver
/// <see cref="TenantSubscription.CompleteUpgradeCharge"/>). Un downgrade no usa esta entidad
/// — ver <see cref="PendingDowngrade"/>. Entidad hija de <see cref="TenantSubscription"/>: su
/// configuración EF requiere ValueGeneratedNever() (ver guardrail de persistencia).
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
    /// <summary>Null = mantener el ciclo de facturación actual, no se pidió cambiarlo.</summary>
    public BillingCycle? ToBillingCycle { get; private set; }
    public PlanChangeRequestStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }

    /// <summary>Precio COMPLETO del plan destino, en centavos — nunca una diferencia
    /// prorrateada. Siempre > 0: un upgrade siempre cobra algo (si el plan destino fuera más
    /// barato o igual, esto es un downgrade y no crea este tipo de request).</summary>
    public long ChargeAmountCents { get; private set; }
    public string ChargeCurrency { get; private set; } = default!;

    /// <summary>Clave de idempotencia del cobro publicado a PaymentApp — la genera el caller
    /// (Application layer), no este agregado, para no acoplar Domain a IdempotencyKeyFactory.</summary>
    public string PaymentIdempotencyKey { get; private set; } = default!;
    public Guid? SaaSPaymentId { get; private set; }

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
        BillingCycle? toBillingCycle,
        Guid requestedByUserId,
        DateTime nowUtc,
        long chargeAmountCents,
        string chargeCurrency,
        string paymentIdempotencyKey)
    {
        if (tenantSubscriptionId == Guid.Empty)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.InvalidSubscription", "TenantSubscriptionId is required."));

        if (tenantId == Guid.Empty)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.InvalidTenant", "TenantId is required."));

        if (fromPlanId == toPlanId && fromPlanVersionId == toPlanVersionId && toBillingCycle is null)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.SamePlan", "Target plan is the same as the current plan."));

        if (chargeAmountCents <= 0)
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.InvalidChargeAmount", "An upgrade must charge a positive amount."));

        if (string.IsNullOrWhiteSpace(chargeCurrency))
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.InvalidCurrency", "Currency is required."));

        if (string.IsNullOrWhiteSpace(paymentIdempotencyKey))
            return Result.Failure<PlanChangeRequest>(new Error("PlanChangeRequest.MissingIdempotencyKey", "An idempotency key is required."));

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
            ToBillingCycle = toBillingCycle,
            Status = PlanChangeRequestStatus.AwaitingPayment,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            ChargeAmountCents = chargeAmountCents,
            ChargeCurrency = chargeCurrency,
            PaymentIdempotencyKey = paymentIdempotencyKey,
        });
    }

    /// <summary>PaymentApp confirmó el cobro — el caller (TenantSubscription) ya aplicó
    /// ChangePlan (y reinició el ciclo) antes de llamar esto; acá solo se cierra el
    /// request.</summary>
    public Result MarkPaymentSucceeded(Guid saaSPaymentId, DateTime nowUtc)
    {
        if (Status != PlanChangeRequestStatus.AwaitingPayment)
            return Result.Failure(new Error("PlanChangeRequest.InvalidTransition", $"Cannot apply from {Status}."));

        Status = PlanChangeRequestStatus.Applied;
        AppliedAtUtc = nowUtc;
        SaaSPaymentId = saaSPaymentId;
        return Result.Success();
    }

    /// <summary>El cobro falló — terminal, sin reintento automático (es un cargo interactivo
    /// iniciado por el usuario, no dunning en background). El plan nunca cambió, no hay nada
    /// que revertir.</summary>
    public Result MarkPaymentFailed(Guid saaSPaymentId, DateTime nowUtc)
    {
        if (Status != PlanChangeRequestStatus.AwaitingPayment)
            return Result.Failure(new Error("PlanChangeRequest.InvalidTransition", $"Cannot fail from {Status}."));

        Status = PlanChangeRequestStatus.PaymentFailed;
        FailedAtUtc = nowUtc;
        SaaSPaymentId = saaSPaymentId;
        return Result.Success();
    }
}
