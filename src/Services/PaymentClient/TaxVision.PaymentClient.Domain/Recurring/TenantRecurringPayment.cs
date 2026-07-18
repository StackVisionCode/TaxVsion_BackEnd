using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Recurring;

/// <summary>
/// Payment plan de un taxpayer — a diferencia de PaymentApp (donde Subscription es dueña del
/// calendario de renovación y PaymentApp solo reacciona a <c>*RenewalDueIntegrationEvent</c>),
/// acá PaymentClient genera y posee su propio calendario, porque no hay ningún otro servicio
/// llevando la cuenta de "cuándo le toca pagar la próxima cuota a este taxpayer" (§Fase H).
///
/// <see cref="PaymentMethodReference"/> es el método tokenizado por el taxpayer una sola vez
/// al crear el plan (SetupIntent en modo off-session) — Fase H no modela un customer/método
/// guardado con historial como PaymentApp (§D); todas las cuotas cobran contra esa misma
/// referencia. Único por <c>(TenantId, TaxpayerId, Purpose)</c>.
/// </summary>
public sealed class TenantRecurringPayment : TenantEntity
{
    private readonly List<RecurringSchedule> _schedules = [];
    private readonly List<RecurringPaymentExecution> _executions = [];

    public Guid TaxpayerId { get; private set; }
    public PaymentProviderCode ProviderCode { get; private set; }
    public string PaymentMethodReference { get; private set; } = default!;
    public Money Amount { get; private set; } = null!;
    public PaymentPurpose Purpose { get; private set; } = null!;
    public BillingCycle BillingCycle { get; private set; }
    public int? CustomIntervalDays { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public int? MaxExecutions { get; private set; }
    public RecurringStatus Status { get; private set; }
    public DateTime? NextExecutionDate { get; private set; }
    public int ExecutionCount { get; private set; }
    public int FailureCount { get; private set; }
    public RetryPolicy RetryPolicy { get; private set; } = null!;
    public long? PlatformFeeAmountCents { get; private set; }
    public string? PlatformFeeReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<RecurringSchedule> Schedules => _schedules;
    public IReadOnlyCollection<RecurringPaymentExecution> Executions => _executions;

    private TenantRecurringPayment() { }

    public static Result<TenantRecurringPayment> Create(
        Guid tenantId,
        Guid taxpayerId,
        PaymentProviderCode providerCode,
        string paymentMethodReference,
        Money amount,
        PaymentPurpose purpose,
        BillingCycle billingCycle,
        int? customIntervalDays,
        DateTime startDate,
        DateTime? endDate,
        int? maxExecutions,
        RetryPolicy retryPolicy,
        long? platformFeeAmountCents,
        string? platformFeeReference,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantRecurringPayment>(
                new Error("TenantRecurringPayment.InvalidTenant", "TenantId is required.")
            );

        if (taxpayerId == Guid.Empty)
            return Result.Failure<TenantRecurringPayment>(
                new Error("TenantRecurringPayment.InvalidTaxpayer", "TaxpayerId is required.")
            );

        if (string.IsNullOrWhiteSpace(paymentMethodReference))
            return Result.Failure<TenantRecurringPayment>(
                new Error("TenantRecurringPayment.InvalidPaymentMethod", "PaymentMethodReference is required.")
            );

        if (amount.AmountCents <= 0)
            return Result.Failure<TenantRecurringPayment>(
                new Error("TenantRecurringPayment.InvalidAmount", "Amount must be greater than zero.")
            );

        if (billingCycle == BillingCycle.Custom && customIntervalDays is null or <= 0)
            return Result.Failure<TenantRecurringPayment>(
                new Error(
                    "TenantRecurringPayment.InvalidInterval",
                    "CustomIntervalDays is required and must be positive for a Custom billing cycle."
                )
            );

        if (endDate is not null && endDate <= startDate)
            return Result.Failure<TenantRecurringPayment>(
                new Error("TenantRecurringPayment.InvalidEndDate", "EndDate must be after StartDate.")
            );

        if (maxExecutions is <= 0)
            return Result.Failure<TenantRecurringPayment>(
                new Error(
                    "TenantRecurringPayment.InvalidMaxExecutions",
                    "MaxExecutions must be greater than zero when provided."
                )
            );

        if (platformFeeAmountCents is { } fee && (fee < 0 || fee > amount.AmountCents))
            return Result.Failure<TenantRecurringPayment>(
                new Error(
                    "TenantRecurringPayment.InvalidPlatformFee",
                    "PlatformFeeAmountCents must be between zero and the plan amount."
                )
            );

        var plan = new TenantRecurringPayment
        {
            TaxpayerId = taxpayerId,
            ProviderCode = providerCode,
            PaymentMethodReference = paymentMethodReference.Trim(),
            Amount = amount,
            Purpose = purpose,
            BillingCycle = billingCycle,
            CustomIntervalDays = billingCycle == BillingCycle.Custom ? customIntervalDays : null,
            StartDate = startDate,
            EndDate = endDate,
            MaxExecutions = maxExecutions,
            Status = RecurringStatus.Active,
            NextExecutionDate = startDate,
            RetryPolicy = retryPolicy,
            PlatformFeeAmountCents = platformFeeAmountCents,
            PlatformFeeReference = platformFeeReference?.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        plan.SetTenant(tenantId);
        return Result.Success(plan);
    }

    /// <summary>Genera hasta <paramref name="count"/> schedules nuevos a partir de
    /// <see cref="NextExecutionDate"/>, avanzando por <see cref="BillingCycle"/> — se detiene
    /// antes si choca con <see cref="EndDate"/> o <see cref="MaxExecutions"/>. Un plan
    /// <c>Cancelled</c>/<c>Completed</c>/no-<c>Active</c> nunca genera schedules nuevos
    /// (invariante §21.1.4 del diseño, portado desde PaymentApp).</summary>
    public Result<IReadOnlyList<Guid>> GenerateSchedules(int count, DateTime nowUtc)
    {
        if (Status != RecurringStatus.Active)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("TenantRecurringPayment.NotActive", $"Cannot generate schedules while {Status}.")
            );

        if (count <= 0)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("TenantRecurringPayment.InvalidCount", "Count must be greater than zero.")
            );

        var created = new List<Guid>();
        var nextDate = NextExecutionDate ?? StartDate;

        for (var i = 0; i < count; i++)
        {
            if (EndDate is not null && nextDate > EndDate)
                break;

            var alreadyCommitted =
                ExecutionCount
                + _schedules.Count(s =>
                    s.Status is not (RecurringScheduleStatus.Failed or RecurringScheduleStatus.Skipped)
                );
            if (MaxExecutions is not null && alreadyCommitted >= MaxExecutions)
                break;

            var schedule = RecurringSchedule.Create(Id, TenantId, nextDate, Amount);
            _schedules.Add(schedule);
            created.Add(schedule.Id);

            nextDate = Advance(nextDate);
        }

        NextExecutionDate = nextDate;
        UpdatedAtUtc = nowUtc;
        return Result.Success<IReadOnlyList<Guid>>(created);
    }

    private DateTime Advance(DateTime date) =>
        BillingCycle switch
        {
            BillingCycle.Monthly => date.AddMonths(1),
            BillingCycle.Quarterly => date.AddMonths(3),
            BillingCycle.Yearly => date.AddYears(1),
            BillingCycle.Custom => date.AddDays(CustomIntervalDays!.Value),
            _ => date.AddMonths(1),
        };

    /// <summary>Reserva el schedule antes de disparar el cobro — lo saca de
    /// <c>Pending</c>/<c>RetryPending</c> para que una segunda pasada del job (p.ej. dos
    /// réplicas sin lock, o un reintento de Wolverine) no lo tome de nuevo mientras el cobro
    /// está en vuelo.</summary>
    public Result MarkScheduleProcessing(Guid scheduleId, Guid actorUserId, DateTime nowUtc)
    {
        var schedule = FindSchedule(scheduleId);
        if (schedule is null)
            return Result.Failure(
                new Error("TenantRecurringPayment.ScheduleNotFound", "RecurringSchedule does not exist.")
            );

        var result = schedule.MarkProcessing();
        if (result.IsFailure)
            return result;

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecordSuccess(
        Guid scheduleId,
        Guid tenantPaymentId,
        string? providerResponse,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var schedule = FindSchedule(scheduleId);
        if (schedule is null)
            return Result.Failure(
                new Error("TenantRecurringPayment.ScheduleNotFound", "RecurringSchedule does not exist.")
            );

        var markResult = schedule.MarkExecuted(tenantPaymentId, providerResponse);
        if (markResult.IsFailure)
            return markResult;

        _executions.Add(
            RecurringPaymentExecution.Record(
                Id,
                schedule.Id,
                TenantId,
                schedule.Amount,
                succeeded: true,
                providerResponse,
                nowUtc
            )
        );

        ExecutionCount++;
        FailureCount = 0;
        Touch(actorUserId, nowUtc);

        if (MaxExecutions is not null && ExecutionCount >= MaxExecutions)
            Complete(nowUtc);

        return Result.Success();
    }

    public Result RecordFailure(Guid scheduleId, string? providerResponse, Guid actorUserId, DateTime nowUtc)
    {
        var schedule = FindSchedule(scheduleId);
        if (schedule is null)
            return Result.Failure(
                new Error("TenantRecurringPayment.ScheduleNotFound", "RecurringSchedule does not exist.")
            );

        _executions.Add(
            RecurringPaymentExecution.Record(
                Id,
                schedule.Id,
                TenantId,
                schedule.Amount,
                succeeded: false,
                providerResponse,
                nowUtc
            )
        );

        if (schedule.RetryCount < RetryPolicy.Backoffs.Count)
        {
            var backoff = RetryPolicy.Backoffs[schedule.RetryCount];
            var retryResult = schedule.MarkRetryPending(nowUtc.Add(backoff), providerResponse);
            if (retryResult.IsFailure)
                return retryResult;

            Touch(actorUserId, nowUtc);
            return Result.Success();
        }

        var failResult = schedule.MarkFailed(providerResponse);
        if (failResult.IsFailure)
            return failResult;

        FailureCount++;
        Touch(actorUserId, nowUtc);

        if (FailureCount >= RetryPolicy.MaxFailures)
            Suspend("Retry backoff exhausted on too many schedules.", actorUserId, nowUtc);

        return Result.Success();
    }

    public Result SkipSchedule(Guid scheduleId, string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantRecurringPayment.InvalidReason", "Reason is required."));

        var schedule = FindSchedule(scheduleId);
        if (schedule is null)
            return Result.Failure(
                new Error("TenantRecurringPayment.ScheduleNotFound", "RecurringSchedule does not exist.")
            );

        var result = schedule.MarkSkipped();
        if (result.IsFailure)
            return result;

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Pause(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != RecurringStatus.Active)
            return Result.Failure(
                new Error("TenantRecurringPayment.InvalidTransition", $"Cannot pause from {Status}.")
            );

        Status = RecurringStatus.Paused;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Reactiva desde <c>Paused</c> (resume normal) o <c>Suspended</c> (reactivación
    /// de admin tras resolver la causa de la suspensión) — el diagrama del diseño dibuja dos
    /// flechas distintas hacia <c>Active</c>, pero el catálogo de métodos no las separa en dos
    /// nombres, así que ambas rutas comparten este método.</summary>
    public Result Resume(Guid actorUserId, DateTime nowUtc)
    {
        if (Status is not (RecurringStatus.Paused or RecurringStatus.Suspended))
            return Result.Failure(
                new Error("TenantRecurringPayment.InvalidTransition", $"Cannot resume from {Status}.")
            );

        Status = RecurringStatus.Active;
        FailureCount = 0;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Suspend(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantRecurringPayment.InvalidReason", "Reason is required."));

        if (Status is not (RecurringStatus.Active or RecurringStatus.Paused))
            return Result.Failure(
                new Error("TenantRecurringPayment.InvalidTransition", $"Cannot suspend from {Status}.")
            );

        Status = RecurringStatus.Suspended;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private void Complete(DateTime nowUtc)
    {
        Status = RecurringStatus.Completed;
        UpdatedAtUtc = nowUtc;
    }

    public Result Cancel(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantRecurringPayment.InvalidReason", "Reason is required."));

        if (Status is RecurringStatus.Completed or RecurringStatus.Cancelled)
            return Result.Failure(
                new Error("TenantRecurringPayment.InvalidTransition", $"Cannot cancel from {Status}.")
            );

        Status = RecurringStatus.Cancelled;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private RecurringSchedule? FindSchedule(Guid scheduleId)
    {
        foreach (var schedule in _schedules)
        {
            if (schedule.Id == scheduleId)
                return schedule;
        }

        return null;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
