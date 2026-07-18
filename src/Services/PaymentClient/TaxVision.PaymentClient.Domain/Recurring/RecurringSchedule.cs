using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Recurring;

/// <summary>Una fecha de cobro dentro de un <see cref="TenantRecurringPayment"/> — entidad
/// hija: su configuración EF requiere <c>ValueGeneratedNever()</c>. Solo
/// <see cref="TenantRecurringPayment"/> la muta, nunca se expone un setter público suelto.</summary>
public sealed class RecurringSchedule : BaseEntity
{
    public Guid TenantRecurringPaymentId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime ScheduledDate { get; private set; }
    public RecurringScheduleStatus Status { get; private set; }
    public Money Amount { get; private set; } = null!;
    public Guid? TenantPaymentId { get; private set; }
    public string? ProviderResponse { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }

    private RecurringSchedule() { }

    public static RecurringSchedule Create(
        Guid tenantRecurringPaymentId,
        Guid tenantId,
        DateTime scheduledDate,
        Money amount
    ) =>
        new()
        {
            TenantRecurringPaymentId = tenantRecurringPaymentId,
            TenantId = tenantId,
            ScheduledDate = scheduledDate,
            Status = RecurringScheduleStatus.Pending,
            Amount = amount,
        };

    public Result MarkProcessing()
    {
        if (Status is not (RecurringScheduleStatus.Pending or RecurringScheduleStatus.RetryPending))
            return Result.Failure(new Error("RecurringSchedule.InvalidTransition", $"Cannot process from {Status}."));

        Status = RecurringScheduleStatus.Processing;
        return Result.Success();
    }

    public Result MarkExecuted(Guid tenantPaymentId, string? providerResponse)
    {
        if (Status != RecurringScheduleStatus.Processing)
            return Result.Failure(new Error("RecurringSchedule.InvalidTransition", $"Cannot execute from {Status}."));

        Status = RecurringScheduleStatus.Executed;
        TenantPaymentId = tenantPaymentId;
        ProviderResponse = providerResponse;
        return Result.Success();
    }

    public Result MarkRetryPending(DateTime nextRetryAtUtc, string? providerResponse)
    {
        if (Status != RecurringScheduleStatus.Processing)
            return Result.Failure(
                new Error("RecurringSchedule.InvalidTransition", $"Cannot schedule a retry from {Status}.")
            );

        Status = RecurringScheduleStatus.RetryPending;
        RetryCount++;
        NextRetryAtUtc = nextRetryAtUtc;
        ProviderResponse = providerResponse;
        return Result.Success();
    }

    public Result MarkFailed(string? providerResponse)
    {
        if (Status != RecurringScheduleStatus.Processing)
            return Result.Failure(new Error("RecurringSchedule.InvalidTransition", $"Cannot fail from {Status}."));

        Status = RecurringScheduleStatus.Failed;
        NextRetryAtUtc = null;
        ProviderResponse = providerResponse;
        return Result.Success();
    }

    public Result MarkSkipped()
    {
        if (Status is not (RecurringScheduleStatus.Pending or RecurringScheduleStatus.RetryPending))
            return Result.Failure(new Error("RecurringSchedule.InvalidTransition", $"Cannot skip from {Status}."));

        Status = RecurringScheduleStatus.Skipped;
        return Result.Success();
    }
}
