using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface ITenantPaymentRepository
{
    Task<TenantPayment?> GetByIdAsync(Guid tenantPaymentId, Guid tenantId, CancellationToken ct = default);
    Task<TenantPayment?> GetByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>A diferencia de PaymentApp, acá el lookup SÍ está scoped por tenant — el
    /// webhook de PaymentClient ya trae el tenant en el path.</summary>
    Task<TenantPayment?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<TenantPayment>> GetStuckProcessingAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Búsqueda cross-tenant paginada para el admin (§42.6/J.2) —
    /// <paramref name="tenantId"/> nulo trae todos los tenants.</summary>
    Task<IReadOnlyList<TenantPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task AddAsync(TenantPayment payment, CancellationToken ct = default);
}
