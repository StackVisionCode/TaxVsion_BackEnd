using TaxVision.Billing.Domain.Payments;

namespace TaxVision.Billing.Application.Abstractions;

public interface IUnappliedPaymentRepository
{
    Task<bool> ExistsAsync(Guid tenantId, Guid tenantPaymentId, CancellationToken ct = default);

    Task<UnappliedPayment?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<UnappliedPayment>> ListByTenantAsync(
        Guid tenantId,
        bool includeResolved,
        int take,
        CancellationToken ct = default
    );

    Task AddAsync(UnappliedPayment payment, CancellationToken ct = default);
}
