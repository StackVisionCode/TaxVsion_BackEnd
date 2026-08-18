using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface ITenantPaymentConfigRepository
{
    Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    );
    Task<TenantPaymentConfig?> GetByIdAsync(Guid tenantPaymentConfigId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Todas las configs del tenant (activas e inactivas), para la pantalla de settings.</summary>
    Task<IReadOnlyList<TenantPaymentConfig>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default);
}
