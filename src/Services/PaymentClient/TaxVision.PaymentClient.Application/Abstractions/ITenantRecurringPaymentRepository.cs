using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface ITenantRecurringPaymentRepository
{
    Task<TenantRecurringPayment?> GetByIdAsync(
        Guid tenantRecurringPaymentId,
        Guid tenantId,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<TenantRecurringPayment>> SearchByTenantAsync(
        Guid tenantId,
        Guid? taxpayerId,
        RecurringStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    /// <summary>Trae, con sus <c>Schedules</c> cargados, todo plan <c>Active</c> con al menos
    /// un schedule <c>Pending</c>/<c>RetryPending</c> vencido — usado por
    /// <c>TenantRecurringExecutionJob</c>/<c>TenantRecurringRetryJob</c>.</summary>
    Task<IReadOnlyList<TenantRecurringPayment>> GetWithDueSchedulesAsync(
        RecurringScheduleStatus scheduleStatus,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );

    Task AddAsync(TenantRecurringPayment plan, CancellationToken ct = default);
}
