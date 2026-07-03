using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Aggregate root representing the commercial subscription of a tenant.
/// Each tenant has exactly one active subscription.
/// Additional seats have their own independent billing cycles.
/// </summary>
public sealed class Subscription : TenantEntity
{
    // ─── PLAN ────────────────────────────────────────────────────────────────

    public Guid PlanId { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public string PlanName { get; private set; } = default!;
    public BillingPeriod BillingPeriod { get; private set; }
    public int IncludedSeats { get; private set; }

    // ─── PENDING PLAN CHANGE ─────────────────────────────────────────────────

    public Guid? PendingPlanId { get; private set; }
    public string? PendingPlanCode { get; private set; }
    public string? PendingPlanName { get; private set; }
    public int? PendingIncludedSeats { get; private set; }

    // ─── CURRENT PERIOD PRICE ────────────────────────────────────────────────

    public Money CurrentBasePrice { get; private set; } = default!;
    public Money CurrentPricePerSeat { get; private set; } = default!;

    // ─── BASE CYCLE ──────────────────────────────────────────────────────────

    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public int BillingAnchorDay { get; private set; }
    public bool IsAutoRenew { get; private set; }

    // ─── TRIAL ───────────────────────────────────────────────────────────────

    public DateTime? TrialEndsAtUtc { get; private set; }

    // ─── STATUS ──────────────────────────────────────────────────────────────

    public SubscriptionStatus Status { get; private set; }
    public Guid? EnrollmentId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // ─── SEATS ───────────────────────────────────────────────────────────────

    private readonly List<SeatSubscription> _seats = [];
    public IReadOnlyList<SeatSubscription> Seats => _seats.AsReadOnly();

    public int PurchasedSeats =>
        _seats.Where(s => s.Status == SeatStatus.Active).Sum(s => s.Quantity);

    public int TotalAvailableSeats => IncludedSeats + PurchasedSeats;

    // ─── TENANT MODULES ──────────────────────────────────────────────────────

    private readonly List<SubscriptionModule> _subscriptionModules = [];
    public IReadOnlyList<SubscriptionModule> SubscriptionModules => _subscriptionModules.AsReadOnly();

    private Subscription() { }

    // ═════════════════════════════════════════════════════════════════════════
    // FACTORIES
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Activates a paid subscription immediately.
    /// Called by TenantCreatedConsumer when EnrollmentId != null.
    /// </summary>
    public static Result<Subscription> Activate(
        Guid tenantId,
        Guid enrollmentId,
        Guid planId,
        string planCode,
        string planName,
        Money currentBasePrice,
        Money currentPricePerSeat,
        BillingPeriod billingPeriod,
        int includedSeats,
        DateTime activatedAtUtc,
        bool autoRenew = true)
    {
        if (tenantId == PlatformTenant.Id)
            return Result.Failure<Subscription>(new Error("Subscription.PlatformTenant",
                "The platform tenant cannot have a commercial subscription."));

        var anchorDay = Math.Min(activatedAtUtc.Day, 28);
        var periodEnd = CalculateNextPeriodEnd(activatedAtUtc, billingPeriod, anchorDay);

        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            PlanCode = planCode,
            PlanName = planName,
            CurrentBasePrice = currentBasePrice,
            CurrentPricePerSeat = currentPricePerSeat,
            BillingPeriod = billingPeriod,
            IncludedSeats = includedSeats,
            PeriodStartUtc = activatedAtUtc,
            PeriodEndUtc = periodEnd,
            BillingAnchorDay = anchorDay,
            IsAutoRenew = autoRenew,
            Status = SubscriptionStatus.Active,
            EnrollmentId = enrollmentId,
            CreatedAtUtc = activatedAtUtc
        };
        sub.SetTenant(tenantId);
        return Result.Success(sub);
    }

    /// <summary>
    /// Creates a trial subscription without charging.
    /// CurrentBasePrice = Money.Zero until the trial converts to paid.
    /// </summary>
    public static Result<Subscription> StartTrial(
        Guid tenantId,
        Guid planId,
        string planCode,
        string planName,
        Money pricePerSeat,
        BillingPeriod billingPeriod,
        int includedSeats,
        int trialDays,
        DateTime startedAtUtc,
        bool autoRenew = true)
    {
        if (tenantId == PlatformTenant.Id)
            return Result.Failure<Subscription>(new Error("Subscription.PlatformTenant",
                "The platform tenant cannot have a commercial subscription."));

        if (trialDays <= 0)
            return Result.Failure<Subscription>(new Error("Subscription.TrialDays",
                "Trial days must be greater than zero."));

        var trialEnd = startedAtUtc.AddDays(trialDays);

        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            PlanCode = planCode,
            PlanName = planName,
            CurrentBasePrice = Money.Zero(pricePerSeat.Currency),
            CurrentPricePerSeat = pricePerSeat,
            BillingPeriod = billingPeriod,
            IncludedSeats = includedSeats,
            PeriodStartUtc = startedAtUtc,
            PeriodEndUtc = trialEnd,
            BillingAnchorDay = Math.Min(startedAtUtc.Day, 28),
            IsAutoRenew = autoRenew,
            TrialEndsAtUtc = trialEnd,
            Status = SubscriptionStatus.Trialing,
            CreatedAtUtc = startedAtUtc
        };
        sub.SetTenant(tenantId);
        return Result.Success(sub);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DOMAIN METHODS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Renews the subscription by extending the billing period.
    /// </summary>
    public Result Renew(DateTime? newEndDate = null)
    {
        var previousEnd = PeriodEndUtc;
        PeriodStartUtc = previousEnd;
        PeriodEndUtc = newEndDate ?? CalculateNextPeriodEnd(previousEnd, BillingPeriod, BillingAnchorDay);
        return Result.Success();
    }

    /// <summary>
    /// Cancels the subscription at the end of the current period.
    /// </summary>
    public Result Cancel(string? reason = null)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.AlreadyCancelled", "Subscription is already cancelled."));

        Status = SubscriptionStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>
    /// Alias for Cancel() — schedules cancellation at period end.
    /// </summary>
    public Result CancelAtPeriodEnd() => Cancel();

    /// <summary>
    /// Marks the subscription PastDue when a renewal payment fails.
    /// Access remains until the end of the current period; retry logic in Payment Service.
    /// </summary>
    public Result MarkPastDue()
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.Cancelled", "Cannot mark a cancelled subscription as past due."));

        Status = SubscriptionStatus.PastDue;
        return Result.Success();
    }

    /// <summary>
    /// Reactivates a PastDue subscription after a successful retry payment.
    /// Extends the period forward from now (Google Workspace model — no backfill of missed days).
    /// </summary>
    public Result Reactivate(DateTime newPeriodEnd)
    {
        if (Status != SubscriptionStatus.PastDue)
            return Result.Failure(new Error("Subscription.NotPastDue", "Only PastDue subscriptions can be reactivated."));

        Status = SubscriptionStatus.Active;
        PeriodStartUtc = DateTime.UtcNow;
        PeriodEndUtc = newPeriodEnd;
        return Result.Success();
    }

    /// <summary>
    /// Applies renewal after successful payment: advances the billing period.
    /// </summary>
    public Result RenewWithPayment(Guid invoiceId, Money newBasePrice, DateTime newPeriodEnd)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.Cancelled", "Cannot renew a cancelled subscription."));

        Status = SubscriptionStatus.Active;
        PeriodStartUtc = PeriodEndUtc;
        PeriodEndUtc = newPeriodEnd;
        CurrentBasePrice = newBasePrice;
        return Result.Success();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PLAN CHANGE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies an approved pending plan or billing-period change.
    /// Called by ApplyPendingChangeHandler after payment confirmation.
    /// No proration — effective immediately from this point forward.
    /// </summary>
    public Result ApplyPlanChange(
        Guid newPlanId,
        string newPlanCode,
        string newPlanName,
        int newIncludedSeats,
        BillingPeriod? newBillingPeriod,
        Money? newBasePrice)
    {
        PlanId = newPlanId;
        PlanCode = newPlanCode;
        PlanName = newPlanName;
        IncludedSeats = newIncludedSeats;
        if (newBillingPeriod.HasValue)
            BillingPeriod = newBillingPeriod.Value;
        if (newBasePrice is not null)
            CurrentBasePrice = newBasePrice;

        // Clear pending change shadow fields
        PendingPlanId = null;
        PendingPlanCode = null;
        PendingPlanName = null;
        PendingIncludedSeats = null;

        return Result.Success();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SEAT MANAGEMENT
    // ═════════════════════════════════════════════════════════════════════════

    public Result<SeatSubscription> RequestSeat(int quantity, DateTime now)
    {
        if (Status != SubscriptionStatus.Active && Status != SubscriptionStatus.Trialing)
            return Result.Failure<SeatSubscription>(new Error("Subscription.NotActive",
                "Cannot add seats to an inactive subscription."));

        if (quantity < 1)
            return Result.Failure<SeatSubscription>(new Error("Subscription.InvalidQuantity",
                "Seat quantity must be at least 1."));

        var seat = SeatSubscription.Create(
            Id, TenantId, quantity, CurrentPricePerSeat, BillingPeriod, now);
        _seats.Add(seat);
        return Result.Success(seat);
    }

    public Result ConfirmSeat(Guid seatId, Guid invoiceId)
    {
        var seat = _seats.FirstOrDefault(s => s.Id == seatId);
        if (seat is null)
            return Result.Failure(new Error("Subscription.SeatNotFound", "Seat not found."));
        seat.Confirm(invoiceId);
        return Result.Success();
    }

    public Result CancelPendingSeat(Guid seatId)
    {
        var seat = _seats.FirstOrDefault(s => s.Id == seatId);
        if (seat is null)
            return Result.Failure(new Error("Subscription.SeatNotFound", "Seat not found."));
        seat.Cancel();
        return Result.Success();
    }

    /// <summary>
    /// Renews a seat after successful payment.
    /// If the seat was marked CancelAtPeriodEnd, cancels it instead of renewing.
    /// </summary>
    public Result RenewSeat(Guid seatId, Guid invoiceId, DateTime newPeriodEnd, Money newPricePerSeat)
    {
        var seat = _seats.FirstOrDefault(s => s.Id == seatId);
        if (seat is null)
            return Result.Failure(new Error("Subscription.SeatNotFound", "Seat not found."));

        if (seat.Status == SeatStatus.CancelAtPeriodEnd)
        {
            seat.Cancel();
            return Result.Success();
        }

        seat.Renew(invoiceId, newPeriodEnd, newPricePerSeat);
        return Result.Success();
    }

    /// <summary>
    /// Marks a seat PastDue when its renewal payment fails.
    /// </summary>
    public Result MarkSeatPastDue(Guid seatId)
    {
        var seat = _seats.FirstOrDefault(s => s.Id == seatId);
        if (seat is null)
            return Result.Failure(new Error("Subscription.SeatNotFound", "Seat not found."));
        seat.MarkPastDue();
        return Result.Success();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates the next billing period end date anchored to a specific day of the month.
    /// AnchorDay is always capped to 28 to avoid end-of-month ambiguities.
    /// Used by seats and the subscription itself.
    /// </summary>
    public static DateTime CalculateNextPeriodEnd(DateTime from, BillingPeriod period, int anchorDay)
    {
        var months = period == BillingPeriod.Annual ? 12 : 1;
        var next = from.AddMonths(months);
        var daysInMonth = DateTime.DaysInMonth(next.Year, next.Month);
        var day = Math.Min(anchorDay, daysInMonth);
        return new DateTime(next.Year, next.Month, day, 0, 0, 0, DateTimeKind.Utc);
    }
}
