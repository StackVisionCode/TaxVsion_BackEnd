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

    /// <summary>Pagos que siguen esperando al proveedor (<c>Processing</c>/<c>RequiresAction</c>) sin cambios
    /// desde <paramref name="stuckBeforeUtc"/> y creados después de <paramref name="createdAfterUtc"/>, de todos
    /// los tenants. Primero los nunca consultados y luego los consultados hace más tiempo (no se acaparan).</summary>
    Task<IReadOnlyList<AwaitingProviderPayment>> GetAwaitingProviderConfirmationAsync(
        DateTime stuckBeforeUtc,
        DateTime createdAfterUtc,
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

    /// <summary>¿La oficina tiene algún pago con ese proveedor? (borrar su config dejaría sin verificar
    /// los webhooks de reembolsos y disputas de esos pagos).</summary>
    Task<bool> AnyForProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default);

    /// <summary>De los proveedores configurados por la oficina, los que tienen al menos un pago (una consulta).</summary>
    Task<IReadOnlySet<PaymentProviderCode>> GetConfiguredProvidersWithPaymentsAsync(
        Guid tenantId,
        CancellationToken ct = default
    );

    /// <summary>¿Hay un cobro sin terminar (Pending/Processing/RequiresAction) para esta referencia externa
    /// (factura)? Tenant explícito: lo llama el resolver público.</summary>
    Task<bool> AnyInProgressForReferenceAsync(
        Guid tenantId,
        string externalReferenceId,
        CancellationToken ct = default
    );

    /// <summary>¿Hay un pago <c>Succeeded</c> de esta referencia externa cobrado después de
    /// <paramref name="sinceUtc"/>? Tenant explícito: lo llama el resolver público.</summary>
    Task<bool> AnySucceededForReferenceSinceAsync(
        Guid tenantId,
        string externalReferenceId,
        DateTime sinceUtc,
        CancellationToken ct = default
    );

    /// <summary>Pagos por id de todos los tenants (reposición de plataforma).</summary>
    Task<IReadOnlyList<TenantPayment>> GetByIdsAcrossTenantsAsync(
        IReadOnlyCollection<Guid> tenantPaymentIds,
        CancellationToken ct = default
    );

    Task AddAsync(TenantPayment payment, CancellationToken ct = default);
}

/// <summary>Lo mínimo para revisar un pago atascado en su propio comando (y su tenant).</summary>
public sealed record AwaitingProviderPayment(Guid TenantPaymentId, Guid TenantId);
