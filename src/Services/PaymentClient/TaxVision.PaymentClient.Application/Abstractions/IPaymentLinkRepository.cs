using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IPaymentLinkRepository
{
    Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Lookup tenant-agnóstico — el checkout público solo tiene el token, el tenant
    /// se deriva del link encontrado.</summary>
    Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default);

    Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid tenantPaymentId, CancellationToken ct = default);

    /// <summary>El link Active más reciente para una referencia externa (factura) del tenant. Lo usa el
    /// resolver estable para reusar un link vigente antes de acuñar uno nuevo. Tenant explícito +
    /// IgnoreQueryFilters (alcanzable desde el resolver público, sin tenant en contexto).</summary>
    Task<PaymentLink?> GetActiveByExternalReferenceAsync(
        Guid tenantId,
        string externalReferenceId,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
        Guid tenantId,
        PaymentLinkStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );

    Task AddAsync(PaymentLink link, CancellationToken ct = default);
}
