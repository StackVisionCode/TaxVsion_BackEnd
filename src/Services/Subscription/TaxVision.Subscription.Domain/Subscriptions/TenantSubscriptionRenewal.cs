using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Renewals;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Intento de renovación de la suscripción base de un tenant. Entidad hija de
/// <see cref="TenantSubscription"/>: su configuración EF requiere ValueGeneratedNever()
/// (ver guardrail de persistencia). <see cref="IdempotencyKey"/> es única — el scheduler
/// no debe crear dos renewals para el mismo período.
/// </summary>
public sealed class TenantSubscriptionRenewal : BaseEntity
{
    public Guid TenantSubscriptionId { get; private set; }
    public Guid TenantId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public RenewalStatus Status { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public DateTime ScheduledAtUtc { get; private set; }
    public DateTime? AttemptedAtUtc { get; private set; }
    public DateTime? SucceededAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public string? ExternalPaymentReference { get; private set; }

    private TenantSubscriptionRenewal() { }

    public static Result<TenantSubscriptionRenewal> Schedule(
        Guid tenantSubscriptionId, Guid tenantId, string idempotencyKey, DateTime periodStartUtc, DateTime periodEndUtc, DateTime nowUtc)
    {
        if (tenantSubscriptionId == Guid.Empty)
            return Result.Failure<TenantSubscriptionRenewal>(new Error("SubscriptionRenewal.InvalidParent", "TenantSubscriptionId is required."));

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<TenantSubscriptionRenewal>(new Error("SubscriptionRenewal.InvalidIdempotencyKey", "IdempotencyKey is required."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure<TenantSubscriptionRenewal>(new Error("SubscriptionRenewal.InvalidPeriod", "Period end must be after period start."));

        return Result.Success(new TenantSubscriptionRenewal
        {
            TenantSubscriptionId = tenantSubscriptionId,
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            Status = RenewalStatus.Scheduled,
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            ScheduledAtUtc = nowUtc,
        });
    }

    public Result MarkProcessing(DateTime nowUtc)
    {
        if (Status is not (RenewalStatus.Scheduled or RenewalStatus.RetryScheduled))
            return Result.Failure(new Error("SubscriptionRenewal.InvalidTransition", $"Cannot process from {Status}."));

        Status = RenewalStatus.Processing;
        AttemptedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkSucceeded(string? externalPaymentReference, DateTime nowUtc)
    {
        if (Status != RenewalStatus.Processing)
            return Result.Failure(new Error("SubscriptionRenewal.InvalidTransition", $"Cannot succeed from {Status}."));

        Status = RenewalStatus.Succeeded;
        SucceededAtUtc = nowUtc;
        ExternalPaymentReference = externalPaymentReference;
        return Result.Success();
    }

    public Result MarkFailed(string failureCode, string failureReason, bool willRetry, DateTime? nextRetryAtUtc, DateTime nowUtc)
    {
        if (Status != RenewalStatus.Processing)
            return Result.Failure(new Error("SubscriptionRenewal.InvalidTransition", $"Cannot fail from {Status}."));

        FailedAtUtc = nowUtc;
        FailureCode = failureCode;
        FailureReason = failureReason;

        if (willRetry)
        {
            Status = RenewalStatus.RetryScheduled;
            RetryCount++;
            NextRetryAtUtc = nextRetryAtUtc;
        }
        else
        {
            Status = RenewalStatus.Failed;
        }

        return Result.Success();
    }
}
