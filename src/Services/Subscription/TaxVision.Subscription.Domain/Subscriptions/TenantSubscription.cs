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
    private readonly List<PendingDowngrade> _pendingDowngrades = [];

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

    /// <summary>Token de concurrencia optimista (rowversion SQL Server). Sin esto, dos
    /// requests concurrentes de RequestUpgrade (p.ej. doble-submit del usuario) pueden pasar
    /// el guard de "ya hay un AwaitingPayment" en memoria a la vez — cada uno lee la misma
    /// foto sin el AwaitingPayment del otro, y ambos confirman su cobro por Stripe. Con el
    /// token, el segundo SaveChangesAsync falla con DbUpdateConcurrencyException en vez de
    /// commitear; Wolverine reintenta el mensaje (ver Policies.OnException en Program.cs) y en
    /// el reintento el guard sí ve el AwaitingPayment ya persistido.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<TenantSubscriptionRenewal> Renewals => _renewals;
    public IReadOnlyCollection<PlanChangeRequest> PlanChangeRequests => _planChangeRequests;
    public IReadOnlyCollection<PendingDowngrade> PendingDowngrades => _pendingDowngrades;

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

    /// <summary><paramref name="newBillingCycle"/> es opcional (self-service activation
    /// puede elegir Monthly/Yearly junto con la activación) — validar que el plan lo soporte
    /// es responsabilidad del caller (mismo contrato que <see cref="ActivateImmediately"/>),
    /// esta entidad no conoce <c>SubscriptionPlanVersion</c> acá.</summary>
    public Result ConvertTrialToActive(
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        BillingCycle? newBillingCycle,
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
        if (newBillingCycle is not null)
            BillingCycle = newBillingCycle.Value;
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

    /// <summary>Sin prorrateo: si <paramref name="newBillingCycle"/> viene informado, se
    /// fija de una — no se recalcula ni se cobra nada por el período en curso. El período
    /// vigente sigue como estaba (ya pago con el ciclo/precio anteriores); la PRÓXIMA
    /// renovación es la que arma el intent con el precio del ciclo nuevo (ver
    /// <see cref="BeginRenewal"/>, que ya resuelve el precio contra <see cref="BillingCycle"/>
    /// vigente en ese momento).</summary>
    public Result ChangePlan(
        SubscriptionPlan newPlan,
        SubscriptionPlanVersion newPlanVersion,
        BillingCycle? newBillingCycle,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot change plan from {Status}."));

        if (newBillingCycle is not null && !newPlanVersion.SupportedBillingCycles.Contains(newBillingCycle.Value))
        {
            return Result.Failure(
                new Error(
                    "Subscription.UnsupportedBillingCycle",
                    $"Plan {newPlan.Code.Value} does not support billing cycle {newBillingCycle.Value}."
                )
            );
        }

        var cycleChanged = newBillingCycle is not null && newBillingCycle.Value != BillingCycle;
        if (PlanId == newPlan.Id && PlanVersionId == newPlanVersion.Id && !cycleChanged)
            return Result.Success();

        PlanId = newPlan.Id;
        PlanVersionId = newPlanVersion.Id;
        PlanCode = newPlan.Code.Value;
        if (newBillingCycle is not null)
            BillingCycle = newBillingCycle.Value;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Solicita un upgrade: SIEMPRE cobra el precio COMPLETO del plan destino
    /// (<paramref name="chargeAmountCents"/>) — nunca una diferencia prorrateada, nunca un
    /// cálculo por días restantes. El plan actual sigue vigente hasta que el pago se confirme:
    /// esto NO aplica <see cref="ChangePlan"/> — solo crea el request en AwaitingPayment. El
    /// caller (Application layer) publica el intent de cobro; recién se aplica cuando
    /// PaymentApp confirma (<see cref="CompleteUpgradeCharge"/>, que también reinicia el ciclo
    /// de facturación desde hoy) o se descarta si falla (<see cref="FailUpgradeCharge"/>).
    /// Rechaza si ya hay un upgrade AwaitingPayment (evita doble cobro por un segundo submit
    /// mientras el primero está en vuelo); si había un downgrade agendado, lo cancela — un
    /// upgrade reemplaza cualquier cambio pendiente.
    /// </summary>
    public Result RequestUpgrade(
        SubscriptionPlan newPlan,
        SubscriptionPlanVersion newPlanVersion,
        BillingCycle? newBillingCycle,
        long chargeAmountCents,
        string chargeCurrency,
        string paymentIdempotencyKey,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot change plan from {Status}."));

        if (newBillingCycle is not null && !newPlanVersion.SupportedBillingCycles.Contains(newBillingCycle.Value))
        {
            return Result.Failure(
                new Error(
                    "Subscription.UnsupportedBillingCycle",
                    $"Plan {newPlan.Code.Value} does not support billing cycle {newBillingCycle.Value}."
                )
            );
        }

        if (_planChangeRequests.Any(r => r.Status == PlanChangeRequestStatus.AwaitingPayment))
            return Result.Failure(
                new Error("PlanChangeRequest.PaymentInProgress", "A plan change payment is already being processed.")
            );

        var cancelDowngradeResult = CancelScheduledDowngradeIfAny(nowUtc);
        if (cancelDowngradeResult.IsFailure)
            return cancelDowngradeResult;

        var requestResult = PlanChangeRequest.Create(
            Id,
            TenantId,
            PlanId,
            PlanVersionId,
            PlanCode,
            newPlan.Id,
            newPlanVersion.Id,
            newPlan.Code.Value,
            newBillingCycle,
            actorUserId,
            nowUtc,
            chargeAmountCents,
            chargeCurrency,
            paymentIdempotencyKey
        );
        if (requestResult.IsFailure)
            return Result.Failure(requestResult.Error);

        _planChangeRequests.Add(requestResult.Value);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>PaymentApp confirmó el cobro completo del upgrade — recién acá se aplica el
    /// cambio de plan (<see cref="ChangePlan"/>) Y se reinicia el ciclo de facturación desde
    /// ahora: es un ciclo completamente nuevo, no una continuación del anterior.</summary>
    public Result CompleteUpgradeCharge(
        Guid requestId,
        SubscriptionPlan toPlan,
        SubscriptionPlanVersion toPlanVersion,
        Guid saaSPaymentId,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var request = FindPlanChangeRequestById(requestId);
        if (request is null)
            return Result.Failure(new Error("PlanChangeRequest.NotFound", "Plan change request does not exist."));

        var switchResult = ChangePlan(toPlan, toPlanVersion, request.ToBillingCycle, actorUserId, nowUtc);
        if (switchResult.IsFailure)
            return switchResult;

        var effectiveCycle = request.ToBillingCycle ?? BillingCycle;
        CurrentPeriodStartUtc = nowUtc;
        CurrentPeriodEndUtc = effectiveCycle.CalculateNext(nowUtc);
        NextRenewalAtUtc = CurrentPeriodEndUtc;

        return request.MarkPaymentSucceeded(saaSPaymentId, nowUtc);
    }

    /// <summary>El cobro del upgrade falló — el plan se queda como estaba, no hay nada que
    /// revertir porque <see cref="ChangePlan"/> nunca se llamó para este request.</summary>
    public Result FailUpgradeCharge(Guid requestId, Guid saaSPaymentId, DateTime nowUtc)
    {
        var request = FindPlanChangeRequestById(requestId);
        if (request is null)
            return Result.Failure(new Error("PlanChangeRequest.NotFound", "Plan change request does not exist."));

        return request.MarkPaymentFailed(saaSPaymentId, nowUtc);
    }

    /// <summary>
    /// Programa un downgrade para el fin del período actual. NUNCA cobra, NUNCA prorratea,
    /// NUNCA genera crédito ni reembolso — el cliente sigue disfrutando el plan actual hasta
    /// la próxima renovación, momento en el que el job de renovación aplica el downgrade (ver
    /// <see cref="ApplyPendingDowngrade"/>) antes de facturar, así que la renovación cobra
    /// normalmente el precio del plan nuevo. Reemplaza cualquier downgrade ya agendado (el más
    /// reciente gana). Rechaza si hay un upgrade AwaitingPayment (evita una carrera entre un
    /// cobro en vuelo y un downgrade).
    /// </summary>
    public Result RequestDowngrade(
        SubscriptionPlan newPlan,
        SubscriptionPlanVersion newPlanVersion,
        BillingCycle? newBillingCycle,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot change plan from {Status}."));

        if (newBillingCycle is not null && !newPlanVersion.SupportedBillingCycles.Contains(newBillingCycle.Value))
        {
            return Result.Failure(
                new Error(
                    "Subscription.UnsupportedBillingCycle",
                    $"Plan {newPlan.Code.Value} does not support billing cycle {newBillingCycle.Value}."
                )
            );
        }

        var cycleChanged = newBillingCycle is not null && newBillingCycle.Value != BillingCycle;
        if (PlanId == newPlan.Id && PlanVersionId == newPlanVersion.Id && !cycleChanged)
            return Result.Success();

        if (_planChangeRequests.Any(r => r.Status == PlanChangeRequestStatus.AwaitingPayment))
            return Result.Failure(
                new Error("PlanChangeRequest.PaymentInProgress", "A plan change payment is already being processed.")
            );

        var cancelResult = CancelScheduledDowngradeIfAny(nowUtc);
        if (cancelResult.IsFailure)
            return cancelResult;

        var requestResult = PendingDowngrade.Create(
            Id,
            TenantId,
            PlanId,
            PlanVersionId,
            PlanCode,
            newPlan.Id,
            newPlanVersion.Id,
            newPlan.Code.Value,
            newBillingCycle,
            actorUserId,
            CurrentPeriodEndUtc,
            nowUtc
        );
        if (requestResult.IsFailure)
            return Result.Failure(requestResult.Error);

        _pendingDowngrades.Add(requestResult.Value);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Aplica un downgrade agendado cuyo período ya terminó. La llama el job de
    /// renovación, ANTES de resolver el precio y facturar — así la renovación cobra el precio
    /// del plan nuevo con normalidad. Nunca se dispara sola.</summary>
    public Result ApplyPendingDowngrade(
        Guid pendingDowngradeId,
        SubscriptionPlan toPlan,
        SubscriptionPlanVersion toPlanVersion,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var pending = FindPendingDowngradeById(pendingDowngradeId);
        if (pending is null)
            return Result.Failure(new Error("PendingDowngrade.NotFound", "Pending downgrade does not exist."));

        if (pending.Status != PendingDowngradeStatus.Scheduled)
            return Result.Failure(
                new Error("PendingDowngrade.InvalidTransition", $"Cannot apply from {pending.Status}.")
            );

        var switchResult = ChangePlan(toPlan, toPlanVersion, pending.ToBillingCycle, actorUserId, nowUtc);
        if (switchResult.IsFailure)
            return switchResult;

        return pending.MarkApplied(nowUtc);
    }

    /// <summary>Cancela un downgrade agendado antes de que se aplique.</summary>
    public Result CancelPendingDowngrade(Guid pendingDowngradeId, Guid actorUserId, DateTime nowUtc)
    {
        var pending = FindPendingDowngradeById(pendingDowngradeId);
        if (pending is null)
            return Result.Failure(new Error("PendingDowngrade.NotFound", "Pending downgrade does not exist."));

        var result = pending.MarkCancelled(nowUtc);
        if (result.IsFailure)
            return result;

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private Result CancelScheduledDowngradeIfAny(DateTime nowUtc)
    {
        var scheduled = FindScheduledDowngrade();
        return scheduled is null ? Result.Success() : scheduled.MarkCancelled(nowUtc);
    }

    private PendingDowngrade? FindScheduledDowngrade()
    {
        foreach (var pending in _pendingDowngrades)
        {
            if (pending.Status == PendingDowngradeStatus.Scheduled)
                return pending;
        }

        return null;
    }

    private PendingDowngrade? FindPendingDowngradeById(Guid pendingDowngradeId)
    {
        foreach (var pending in _pendingDowngrades)
        {
            if (pending.Id == pendingDowngradeId)
                return pending;
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

    /// <summary>Programa el primer cobro real de una suscripción recién convertida de
    /// Trialing a Active (activación self-service, antes de que termine el trial). A
    /// diferencia de <see cref="BeginRenewal"/> (que avanza a un período NUEVO a partir de
    /// uno ya vigente), esto cobra por el período que <see cref="ConvertTrialToActive"/>
    /// acaba de fijar como vigente — llamar a este método sin haber convertido antes el
    /// trial deja el período mal facturado.</summary>
    public Result BeginActivationCharge(string idempotencyKey, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Active)
            return Result.Failure(
                new Error("Subscription.InvalidTransition", $"Cannot begin activation charge from {Status}.")
            );

        if (FindRenewalByKey(idempotencyKey) is not null)
            return Result.Success();

        var renewalResult = TenantSubscriptionRenewal.Schedule(
            Id,
            TenantId,
            idempotencyKey,
            CurrentPeriodStartUtc,
            CurrentPeriodEndUtc,
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

    /// <summary>Cantidad máxima de reintentos de renovación que este agregado tolera antes de
    /// forzar PastDue por cuenta propia, sin importar lo que PaymentApp reporte en
    /// <c>WillRetry</c>. PaymentApp puede reportarlo en true indefinidamente para fallos que
    /// nunca llegan a Processing (p.ej. sin payment method guardado), así que este cap actúa
    /// como red de seguridad para no quedar Active para siempre.</summary>
    private const int MaxRenewalRetryAttempts = 3;

    /// <summary>FailureCodes que PaymentApp puede devolver y que son permanentes (no tiene
    /// sentido reintentar): fuerzan PastDue en el primer intento fallido, sin esperar el cap
    /// de <see cref="MaxRenewalRetryAttempts"/>.</summary>
    private static readonly HashSet<string> PermanentRenewalFailureCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "requires_payment_method",
    };

    /// <summary>Registra el fallo de una renovación. Si <paramref name="willRetry"/> es
    /// false agota los reintentos y transiciona la suscripción a PastDue.
    ///
    /// No confía ciegamente en <paramref name="willRetry"/>: se lo sobreescribe a false (y por
    /// lo tanto se fuerza PastDue) cuando el failureCode es conocido como permanente, o cuando
    /// el renewal ya agotó <see cref="MaxRenewalRetryAttempts"/> reintentos propios — esto
    /// cubre el caso en que PaymentApp reporta WillRetry=true de forma incorrecta.</summary>
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

        var effectiveWillRetry =
            willRetry
            && renewal.RetryCount < MaxRenewalRetryAttempts
            && !PermanentRenewalFailureCodes.Contains(failureCode);

        var markResult = MarkRenewalAttemptFailed(
            renewal,
            failureCode,
            failureReason,
            effectiveWillRetry,
            effectiveWillRetry ? nextRetryAtUtc : null,
            nowUtc
        );
        if (markResult.IsFailure)
            return markResult;

        return effectiveWillRetry
            ? Result.Success()
            : MarkPastDueBecauseRenewalFailed(failureCode, actorUserId, nowUtc);
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
