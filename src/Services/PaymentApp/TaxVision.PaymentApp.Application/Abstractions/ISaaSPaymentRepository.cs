using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface ISaaSPaymentRepository
{
    Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 17) — resuelve por Id sin conocer el tenant: usado por
    /// <c>OnboardingRefundConsumer</c>, que solo recibe el <c>PaymentId</c> de un pago
    /// <see cref="SaaSPaymentType.OnboardingInitial"/> creado con <c>TenantId=Guid.Empty</c>
    /// (mismo sentinel documentado en <see cref="SaaSPayment.CreateForOnboarding"/>).</summary>
    Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default);

    Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>Resuelve el aggregate a partir de la referencia opaca que el provider
    /// devuelve en un webhook — el único caso donde se busca sin conocer el TenantId de
    /// antemano (§41.15/§1637 del diseño: el tenant se saca del pago encontrado, no al
    /// revés).</summary>
    Task<SaaSPayment?> GetByExternalReferenceAsync(
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    );

    /// <summary>Pagos atascados en Processing más allá de <paramref name="cutoffUtc"/> —
    /// batch job query, cross-tenant por diseño, solo la llama
    /// <c>PendingChargeReconciliationJob</c> (§1714 del diseño).</summary>
    Task<IReadOnlyList<SaaSPayment>> GetStuckProcessingAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Cobros confirmados de un tenant real que siguen sin recibo pasado
    /// <paramref name="cutoffUtc"/> — batch job query, cross-tenant por diseño, solo la llama
    /// <c>MissingReceiptBackfillJob</c>.</summary>
    Task<IReadOnlyList<SaaSPayment>> GetSucceededWithoutReceiptAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Pagos Failed con un retry ya vencido — batch job query, cross-tenant por
    /// diseño, solo la llama <c>DunningJob</c>.</summary>
    Task<IReadOnlyList<SaaSPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );

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
    /// <summary>El pago de un onboarding, buscado por su OnboardingId. Ignora el filtro de tenant: la fila
    /// todavía vive con <c>TenantId = Guid.Empty</c> hasta que se re-hospeda.</summary>
    Task<SaaSPayment?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);

    /// <summary>Pago buscado por el id de Checkout Session del provider (<c>cs_...</c> en Stripe, OrderId en
    /// PayPal). Se usa para desambiguar el provider en el cancel M2M cuando el caller no lo trae: el adapter
    /// correcto sale del <see cref="SaaSPayment.ProviderCode"/>.</summary>
    Task<SaaSPayment?> GetByProviderCheckoutSessionIdAsync(string providerSessionId, CancellationToken ct = default);

    /// <summary>Historial de pagos del tenant, el más reciente primero. A diferencia de
    /// <see cref="SearchAdminAsync"/>, el tenant NO es opcional y no se traen los intentos: el cuerpo crudo
    /// del proveedor no sale de admin.</summary>
    Task<(IReadOnlyList<SaaSPayment> Items, int TotalCount)> SearchForTenantAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<SaaSPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        SaaSPaymentType? type,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task AddAsync(SaaSPayment payment, CancellationToken ct = default);

    /// <summary>Para cada <c>(Type, TargetAggregateId)</c> del tenant, el <c>CreatedAtUtc</c> del pago
    /// Succeeded más reciente. Lo usa el billing history para no mostrar "Pay now" sobre filas Failed
    /// cuyo cobro ya fue saldado por un pago posterior (p. ej. el usuario pagó manualmente dos días
    /// después y la fila vieja Failed quedó zombi).</summary>
    Task<
        IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime>
    > GetLatestSucceededCreatedAtByTargetsAsync(
        Guid tenantId,
        IReadOnlyCollection<(SaaSPaymentType Type, Guid TargetAggregateId)> targets,
        CancellationToken ct = default
    );

    /// <summary>Para cada <paramref name="types"/>, el <c>CreatedAtUtc</c> del Succeeded más reciente del
    /// tenant, ignorando <c>TargetAggregateId</c>. Lo usa el billing history para las familias cuyo
    /// pago vive a nivel de tenant (p. ej. subscription renewals: un off-session Failed con un
    /// target A puede quedar saldado por un checkout Succeeded con un target B — ambos pertenecen a
    /// la única TenantSubscription del tenant, así que agruparlos por target perdería la
    /// equivalencia y la fila Failed quedaría zombie).</summary>
    Task<IReadOnlyDictionary<SaaSPaymentType, DateTime>> GetLatestSucceededCreatedAtByTypesAsync(
        Guid tenantId,
        IReadOnlyCollection<SaaSPaymentType> types,
        CancellationToken ct = default
    );
}
