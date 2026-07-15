using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Derecho comercial y de acceso de un tenant a la plataforma. Un registro activo por
/// tenant. Independiente de los asientos (<see cref="Seats.SubscriptionSeat"/>): renovar
/// esta suscripción no renueva seats, y viceversa. Cada transición de estado es un método
/// explícito con su propia precondición — no existe un ChangeStatus(...) genérico.
/// </summary>
public sealed class TenantSubscription : TenantEntity
{
    private readonly List<TenantSubscriptionRenewal> _renewals = [];
    private readonly List<PlanChangeRequest> _planChangeRequests = [];

    public Guid PlanId { get; private set; }
    public Guid PlanVersionId { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public SubscriptionStatus Status { get; private set; }
    public BillingCycle BillingCycle { get; private set; }

    public DateTime CurrentPeriodStartUtc { get; private set; }
    public DateTime CurrentPeriodEndUtc { get; private set; }
    public DateTime? NextRenewalAtUtc { get; private set; }
    public DateTime? TrialEndsAtUtc { get; private set; }
    public DateTime? GracePeriodEndsAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? SuspensionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<TenantSubscriptionRenewal> Renewals => _renewals;
    public IReadOnlyCollection<PlanChangeRequest> PlanChangeRequests => _planChangeRequests;

    private TenantSubscription() { }

    public static Result<TenantSubscription> StartTrial(
        Guid tenantId,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        int trialDays,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var tenantGuard = EnsureTenant(tenantId);
        if (tenantGuard.IsFailure)
            return Result.Failure<TenantSubscription>(tenantGuard.Error);

        if (trialDays is < 1 or > 90)
            return Result.Failure<TenantSubscription>(
                new Error("Subscription.InvalidTrialDays", "Trial days must be between 1 and 90.")
            );

        var subscription = new TenantSubscription
        {
            PlanId = plan.Id,
            PlanVersionId = planVersion.Id,
            PlanCode = plan.Code.Value,
            Status = SubscriptionStatus.Trialing,
            BillingCycle = BillingCycle.Monthly,
            CurrentPeriodStartUtc = nowUtc,
            CurrentPeriodEndUtc = nowUtc.AddDays(trialDays),
            NextRenewalAtUtc = nowUtc.AddDays(trialDays),
            TrialEndsAtUtc = nowUtc.AddDays(trialDays),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        subscription.SetTenant(tenantId);
        return Result.Success(subscription);
    }

    public static Result<TenantSubscription> ActivateImmediately(
        Guid tenantId,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        BillingCycle billingCycle,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var tenantGuard = EnsureTenant(tenantId);
        if (tenantGuard.IsFailure)
            return Result.Failure<TenantSubscription>(tenantGuard.Error);

        if (periodEndUtc <= periodStartUtc)
        {
            return Result.Failure<TenantSubscription>(
                new Error("Subscription.InvalidPeriod", "Period end must be after period start.")
            );
        }

        var subscription = new TenantSubscription
        {
            PlanId = plan.Id,
            PlanVersionId = planVersion.Id,
            PlanCode = plan.Code.Value,
            Status = SubscriptionStatus.Active,
            BillingCycle = billingCycle,
            CurrentPeriodStartUtc = periodStartUtc,
            CurrentPeriodEndUtc = periodEndUtc,
            NextRenewalAtUtc = periodEndUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        subscription.SetTenant(tenantId);
        return Result.Success(subscription);
    }

    public Result ConvertTrialToActive(
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != SubscriptionStatus.Trialing)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot convert trial from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Subscription.InvalidPeriod", "Period end must be after period start."));

        Status = SubscriptionStatus.Active;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
        TrialEndsAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireTrialWithoutConversion(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Trialing)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot expire trial from {Status}."));

        Status = SubscriptionStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ChangePlan(
        SubscriptionPlan newPlan,
        SubscriptionPlanVersion newPlanVersion,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot change plan from {Status}."));

        if (PlanId == newPlan.Id && PlanVersionId == newPlanVersion.Id)
            return Result.Success();

        PlanId = newPlan.Id;
        PlanVersionId = newPlanVersion.Id;
        PlanCode = newPlan.Code.Value;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Solicita un cambio de plan. En modo Immediate lo aplica ya (vía <see cref="ChangePlan"/>)
    /// y deja el request marcado como Applied para el historial. En modo EndOfPeriod solo
    /// lo encola — no calcula prorrateo ni cobra nada; el plan nuevo se aplica solo cuando
    /// termine el período actual (ver <see cref="ApplyPendingPlanChange"/>), y el precio del
    /// plan nuevo se cobra con normalidad en esa renovación. Reemplaza cualquier solicitud
    /// pendiente anterior (la más reciente gana).
    /// </summary>
    public Result RequestPlanChange(
        SubscriptionPlan newPlan,
        SubscriptionPlanVersion newPlanVersion,
        PlanChangeEffectiveMode mode,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot change plan from {Status}."));

        if (PlanId == newPlan.Id && PlanVersionId == newPlanVersion.Id)
            return Result.Success();

        var supersedeResult = SupersedePendingPlanChangeRequest(nowUtc);
        if (supersedeResult.IsFailure)
            return supersedeResult;

        var effectiveAtUtc = mode == PlanChangeEffectiveMode.Immediate ? nowUtc : CurrentPeriodEndUtc;
        var requestResult = PlanChangeRequest.Create(
            Id,
            TenantId,
            PlanId,
            PlanVersionId,
            PlanCode,
            newPlan.Id,
            newPlanVersion.Id,
            newPlan.Code.Value,
            mode,
            actorUserId,
            effectiveAtUtc,
            nowUtc
        );
        if (requestResult.IsFailure)
            return Result.Failure(requestResult.Error);

        var request = requestResult.Value;
        _planChangeRequests.Add(request);

        if (mode != PlanChangeEffectiveMode.Immediate)
            return Result.Success();

        var switchResult = ChangePlan(newPlan, newPlanVersion, actorUserId, nowUtc);
        if (switchResult.IsFailure)
            return switchResult;

        return request.MarkApplied(nowUtc);
    }

    /// <summary>Aplica una solicitud de cambio de plan diferida cuyo período ya terminó.
    /// La llama el job de renovación, no un caller directo — nunca se dispara sola.</summary>
    public Result ApplyPendingPlanChange(
        Guid requestId,
        SubscriptionPlan toPlan,
        SubscriptionPlanVersion toPlanVersion,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var request = FindPlanChangeRequestById(requestId);
        if (request is null)
            return Result.Failure(new Error("PlanChangeRequest.NotFound", "Plan change request does not exist."));

        if (request.Status != PlanChangeRequestStatus.Pending)
            return Result.Failure(
                new Error("PlanChangeRequest.InvalidTransition", $"Cannot apply from {request.Status}.")
            );

        var switchResult = ChangePlan(toPlan, toPlanVersion, actorUserId, nowUtc);
        if (switchResult.IsFailure)
            return switchResult;

        return request.MarkApplied(nowUtc);
    }

    /// <summary>Cancela una solicitud de cambio de plan diferida antes de que se aplique.</summary>
    public Result CancelPendingPlanChange(Guid requestId, Guid actorUserId, DateTime nowUtc)
    {
        var request = FindPlanChangeRequestById(requestId);
        if (request is null)
            return Result.Failure(new Error("PlanChangeRequest.NotFound", "Plan change request does not exist."));

        var result = request.MarkCancelled(nowUtc);
        if (result.IsFailure)
            return result;

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private Result SupersedePendingPlanChangeRequest(DateTime nowUtc)
    {
        var pending = FindPendingPlanChangeRequest();
        return pending is null ? Result.Success() : pending.MarkCancelled(nowUtc);
    }

    private PlanChangeRequest? FindPendingPlanChangeRequest()
    {
        foreach (var request in _planChangeRequests)
        {
            if (request.Status == PlanChangeRequestStatus.Pending)
                return request;
        }

        return null;
    }

    private PlanChangeRequest? FindPlanChangeRequestById(Guid requestId)
    {
        foreach (var request in _planChangeRequests)
        {
            if (request.Id == requestId)
                return request;
        }

        return null;
    }

    public Result MarkPastDueBecauseRenewalFailed(string failureCode, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Active)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot mark past due from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("Subscription.InvalidFailureCode", "FailureCode is required."));

        Status = SubscriptionStatus.PastDue;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromPastDue(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.PastDue)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot recover from {Status}."));

        Status = SubscriptionStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result EnterGracePeriodAfterRetriesExhausted(DateTime graceEndsAtUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.PastDue)
            return Result.Failure(
                new Error("Subscription.InvalidTransition", $"Cannot enter grace period from {Status}.")
            );

        if (graceEndsAtUtc <= nowUtc)
            return Result.Failure(
                new Error("Subscription.InvalidGracePeriod", "GraceEndsAtUtc must be in the future.")
            );

        Status = SubscriptionStatus.GracePeriod;
        GracePeriodEndsAtUtc = graceEndsAtUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromGracePeriod(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.GracePeriod)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot recover from {Status}."));

        Status = SubscriptionStatus.Active;
        GracePeriodEndsAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendBecauseGraceExpired(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.GracePeriod)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot suspend from {Status}."));

        Status = SubscriptionStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = "GraceExpired";
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendForPolicyViolation(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (
            !IsOneOf(
                Status,
                SubscriptionStatus.Trialing,
                SubscriptionStatus.Active,
                SubscriptionStatus.PastDue,
                SubscriptionStatus.GracePeriod
            )
        )
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot suspend from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Subscription.InvalidReason", "Reason is required."));

        Status = SubscriptionStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ReactivateAfterAdminReview(
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != SubscriptionStatus.Suspended)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot reactivate from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Subscription.InvalidPeriod", "Period end must be after period start."));

        Status = SubscriptionStatus.Active;
        SuspendedAtUtc = null;
        SuspensionReason = null;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterSuspensionTimeout(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Suspended)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot expire from {Status}."));

        Status = SubscriptionStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result CancelImmediately(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (IsOneOf(Status, SubscriptionStatus.Cancelled, SubscriptionStatus.Expired))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Subscription.InvalidReason", "Reason is required."));

        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterCancellationPeriodEnded(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot expire from {Status}."));

        Status = SubscriptionStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Programa una renovación de la suscripción base. Idempotente por
    /// <paramref name="idempotencyKey"/> — un segundo intento con la misma key no crea un
    /// renewal duplicado.</summary>
    public Result BeginRenewal(string idempotencyKey, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Active)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot begin renewal from {Status}."));

        if (FindRenewalByKey(idempotencyKey) is not null)
            return Result.Success();

        var newPeriodEndUtc = BillingCycle.CalculateNext(CurrentPeriodEndUtc);
        var renewalResult = TenantSubscriptionRenewal.Schedule(
            Id,
            TenantId,
            idempotencyKey,
            CurrentPeriodEndUtc,
            newPeriodEndUtc,
            nowUtc
        );
        if (renewalResult.IsFailure)
            return Result.Failure(renewalResult.Error);

        _renewals.Add(renewalResult.Value);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Aplica una renovación exitosa: avanza el período de la suscripción base al
    /// que quedó agendado en el renewal. No toca seats ni add-ons.</summary>
    public Result CompleteRenewal(Guid renewalId, string? externalPaymentReference, Guid actorUserId, DateTime nowUtc)
    {
        var renewal = FindRenewalById(renewalId);
        if (renewal is null)
            return Result.Failure(new Error("Subscription.RenewalNotFound", "Renewal does not exist."));

        var succeeded = CompleteRenewalAttempt(renewal, externalPaymentReference, nowUtc);
        if (succeeded.IsFailure)
            return succeeded;

        if (Status != SubscriptionStatus.Active)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot apply renewal while {Status}."));

        CurrentPeriodStartUtc = renewal.PeriodStartUtc;
        CurrentPeriodEndUtc = renewal.PeriodEndUtc;
        NextRenewalAtUtc = renewal.PeriodEndUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Registra el fallo de una renovación. Si <paramref name="willRetry"/> es
    /// false agota los reintentos y transiciona la suscripción a PastDue.</summary>
    public Result FailRenewal(
        Guid renewalId,
        string failureCode,
        string failureReason,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var renewal = FindRenewalById(renewalId);
        if (renewal is null)
            return Result.Failure(new Error("Subscription.RenewalNotFound", "Renewal does not exist."));

        var markResult = MarkRenewalAttemptFailed(
            renewal,
            failureCode,
            failureReason,
            willRetry,
            nextRetryAtUtc,
            nowUtc
        );
        if (markResult.IsFailure)
            return markResult;

        return willRetry ? Result.Success() : MarkPastDueBecauseRenewalFailed(failureCode, actorUserId, nowUtc);
    }

    private static Result CompleteRenewalAttempt(
        TenantSubscriptionRenewal renewal,
        string? externalPaymentReference,
        DateTime nowUtc
    )
    {
        if (renewal.Status != RenewalStatus.Processing)
        {
            var processing = renewal.MarkProcessing(nowUtc);
            if (processing.IsFailure)
                return processing;
        }

        return renewal.MarkSucceeded(externalPaymentReference, nowUtc);
    }

    private static Result MarkRenewalAttemptFailed(
        TenantSubscriptionRenewal renewal,
        string failureCode,
        string failureReason,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        DateTime nowUtc
    )
    {
        if (renewal.Status != RenewalStatus.Processing)
        {
            var processing = renewal.MarkProcessing(nowUtc);
            if (processing.IsFailure)
                return processing;
        }

        return renewal.MarkFailed(failureCode, failureReason, willRetry, nextRetryAtUtc, nowUtc);
    }

    private TenantSubscriptionRenewal? FindRenewalByKey(string idempotencyKey)
    {
        foreach (var renewal in _renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }

    private TenantSubscriptionRenewal? FindRenewalById(Guid renewalId)
    {
        foreach (var renewal in _renewals)
        {
            if (renewal.Id == renewalId)
                return renewal;
        }

        return null;
    }

    private static Result EnsureTenant(Guid tenantId) =>
        tenantId == Guid.Empty
            ? Result.Failure(new Error("Subscription.InvalidTenant", "TenantId is required."))
            : Result.Success();

    private static bool IsOneOf(SubscriptionStatus value, params SubscriptionStatus[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (value == candidate)
                return true;
        }

        return false;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
