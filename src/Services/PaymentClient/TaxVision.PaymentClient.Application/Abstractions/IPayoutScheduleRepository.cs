using TaxVision.PaymentClient.Domain.Payouts;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IPayoutScheduleRepository
{
    Task<PayoutSchedule?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<PayoutSchedule?> GetByTenantConnectAccountIdAsync(Guid tenantConnectAccountId, CancellationToken ct = default);
    Task AddAsync(PayoutSchedule schedule, CancellationToken ct = default);
}
