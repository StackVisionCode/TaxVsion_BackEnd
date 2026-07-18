using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IPaymentLinkRepository
{
    Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Lookup tenant-agnóstico — el checkout público solo tiene el token, el tenant
    /// se deriva del link encontrado.</summary>
    Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default);

    Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid tenantPaymentId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
        Guid tenantId, PaymentLinkStatus? status, int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);

    Task AddAsync(PaymentLink link, CancellationToken ct = default);
}
