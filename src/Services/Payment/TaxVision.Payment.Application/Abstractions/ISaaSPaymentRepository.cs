using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Application.Abstractions;

public interface ISaaSPaymentRepository
{
    Task<SaaSPayment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SaaSPayment?> GetByReferenceIdAsync(Guid referenceId, SaaSPaymentType type, CancellationToken ct = default);
    Task AddAsync(SaaSPayment payment, CancellationToken ct = default);
    Task<List<SaaSPayment>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
