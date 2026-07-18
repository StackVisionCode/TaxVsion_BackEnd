using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface ISaaSPaymentRepository
{
    Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default);
    Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>Resuelve el aggregate a partir de la referencia opaca que el provider
    /// devuelve en un webhook — el único caso donde se busca sin conocer el TenantId de
    /// antemano (§41.15/§1637 del diseño: el tenant se saca del pago encontrado, no al
    /// revés).</summary>
    Task<SaaSPayment?> GetByExternalReferenceAsync(PaymentProviderCode code, string providerChargeReference, CancellationToken ct = default);

    /// <summary>Pagos atascados en Processing más allá de <paramref name="cutoffUtc"/> —
    /// batch job query, cross-tenant por diseño, solo la llama
    /// <c>PendingChargeReconciliationJob</c> (§1714 del diseño).</summary>
    Task<IReadOnlyList<SaaSPayment>> GetStuckProcessingAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);

    /// <summary>Pagos Failed con un retry ya vencido — batch job query, cross-tenant por
    /// diseño, solo la llama <c>DunningJob</c>.</summary>
    Task<IReadOnlyList<SaaSPayment>> GetDueForRetryAsync(DateTime nowUtc, int batchSize, CancellationToken ct = default);

    /// <summary>Cuenta de pagos Failed con retry vencido, sin cargar las entidades — alimenta
    /// el gauge <c>paymentapp.dunning.queue_depth</c> (§29.2/J.1).</summary>
    Task<int> CountDueForRetryAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Suma de <see cref="SaaSPayment.Amount"/> de pagos <c>Succeeded</c> de un tipo
    /// desde <paramref name="sinceUtc"/> — proxy de MRR que PaymentApp puede calcular con sus
    /// propios datos (no conoce el calendario de recurrencia real, eso lo tiene Subscription;
    /// §29.2/J.1: <c>paymentapp.mrr_usd</c>).</summary>
    Task<long> SumSucceededAmountCentsAsync(SaaSPaymentType type, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Búsqueda cross-tenant paginada para el admin (§42.6/J.2) —
    /// <paramref name="tenantId"/> nulo trae todos los tenants.</summary>
    Task<IReadOnlyList<SaaSPayment>> SearchAdminAsync(
        Guid? tenantId, PaymentStatus? status, SaaSPaymentType? type, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(SaaSPayment payment, CancellationToken ct = default);
}
