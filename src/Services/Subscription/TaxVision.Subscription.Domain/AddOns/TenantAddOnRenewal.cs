using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Renewals;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>
/// Intento de renovación de un add-on, independiente de la suscripción base y de los
/// seats. Entidad hija de <see cref="TenantAddOn"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class TenantAddOnRenewal : BaseEntity
{
    public Guid TenantAddOnId { get; private set; }
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

    private TenantAddOnRenewal() { }

    public static Result<TenantAddOnRenewal> Schedule(
        Guid tenantAddOnId, Guid tenantId, string idempotencyKey, DateTime periodStartUtc, DateTime periodEndUtc, DateTime nowUtc)
    {
        if (tenantAddOnId == Guid.Empty)
            return Result.Failure<TenantAddOnRenewal>(new Error("AddOnRenewal.InvalidParent", "TenantAddOnId is required."));

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<TenantAddOnRenewal>(new Error("AddOnRenewal.InvalidIdempotencyKey", "IdempotencyKey is required."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure<TenantAddOnRenewal>(new Error("AddOnRenewal.InvalidPeriod", "Period end must be after period start."));

        return Result.Success(new TenantAddOnRenewal
        {
            TenantAddOnId = tenantAddOnId,
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
            return Result.Failure(new Error("AddOnRenewal.InvalidTransition", $"Cannot process from {Status}."));

        Status = RenewalStatus.Processing;
        AttemptedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkSucceeded(string? externalPaymentReference, DateTime nowUtc)
    {
        if (Status != RenewalStatus.Processing)
            return Result.Failure(new Error("AddOnRenewal.InvalidTransition", $"Cannot succeed from {Status}."));

        Status = RenewalStatus.Succeeded;
        SucceededAtUtc = nowUtc;
        ExternalPaymentReference = externalPaymentReference;
        return Result.Success();
    }

    public Result MarkFailed(string failureCode, string failureReason, bool willRetry, DateTime? nextRetryAtUtc, DateTime nowUtc)
    {
        if (Status != RenewalStatus.Processing)
            return Result.Failure(new Error("AddOnRenewal.InvalidTransition", $"Cannot fail from {Status}."));

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
