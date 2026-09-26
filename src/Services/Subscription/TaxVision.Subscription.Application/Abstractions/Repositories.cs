using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IPlanRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default);
    Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default);

    /// <summary>Plan trackeado (con versiones e hijas) para autoría; el resto de reads son AsNoTracking.</summary>
    Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid planId, CancellationToken ct = default);
}

public interface ISubscriptionRepository
{
    Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantSubscription subscription, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 16) — idempotencia de internal/subscriptions/activate-from-onboarding.</summary>
    Task<TenantSubscription?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);

    /// <summary>Active subscriptions whose NextRenewalAtUtc has passed. Batch job query —
    /// crosses tenants intentionally (only the scheduler calls this, never a tenant-scoped handler).</summary>
    Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Suscripciones con cancelación programada cuyo acceso termina en la ventana dada. No son las
    /// mismas que las de <see cref="GetRenewingBetweenAsync"/>: éstas no renuevan, terminan.</summary>
    Task<IReadOnlyList<TenantSubscription>> GetAccessEndingBetweenAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Admin cross-tenant query — only PlatformAdmin endpoints call this.</summary>
    Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    /// <summary>Ids de tenants suscritos a un plan, paginado por keyset (TenantId &gt; afterTenantId).
    /// Cross-tenant — lo usa el recálculo masivo de entitlements.</summary>
    Task<IReadOnlyList<Guid>> GetTenantIdsByPlanAsync(
        Guid planId,
        Guid afterTenantId,
        int batchSize,
        CancellationToken ct = default
    );
}

public interface ISubscriptionSeatRepository
{
    Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<SubscriptionSeat?> GetByCurrentUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Como GetByCurrentUserIdAsync pero TRACKED — para liberar el asiento desde un consumer
    /// (la variante AsNoTracking no persistiría la mutación).</summary>
    Task<SubscriptionSeat?> GetTrackedByCurrentUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default);

    /// <summary>Batch job queries — cross-tenant by design, only the scheduler calls these.</summary>
    Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Admin cross-tenant query — only PlatformAdmin endpoints call this.</summary>
    Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}

/// <summary>Intenciones de compra de asientos por hosted-checkout (el camino redirect, sin método en
/// archivo). Molde: el <c>TenantOnboarding</c> de Auth para la compra por checkout.</summary>
public interface ISeatPurchaseIntentRepository
{
    Task AddAsync(SeatPurchaseIntent intent, CancellationToken ct = default);

    /// <summary>Lectura tenant-scoped (endpoint de estado del tenant).</summary>
    Task<SeatPurchaseIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default);

    /// <summary>La compra de asientos que el tenant dejó a medias y todavía se puede pagar (<c>Pending</c> con
    /// sesión de checkout sin caducar). Es el guard contra el doble cobro al reintentar.</summary>
    Task<SeatPurchaseIntent?> GetOpenByTenantAsync(Guid tenantId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Cross-tenant para el consumer del webhook: ignora el filtro fail-closed; el caller valida el
    /// tenant del evento antes de mutar (mismo patrón que los consumers de resultado de pago).</summary>
    Task<SeatPurchaseIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default);

    /// <summary>Cross-tenant (job de reconciliación): intenciones <c>Pending</c> con pago ya emitido
    /// (<c>SaaSPaymentId</c> asignado) creadas antes de <paramref name="olderThanUtc"/> — las que pudieron
    /// quedar sin aprovisionar si se perdió el evento de resultado. Ignora el filtro fail-closed; el job
    /// arrastra el <c>TenantId</c> de cada fila.</summary>
    Task<IReadOnlyList<SeatPurchaseIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    );
}

/// <summary>Intenciones de compra de add-ons por hosted-checkout. Espejo de
/// <see cref="ISeatPurchaseIntentRepository"/>; el guard de doble cobro es por add-on, no por tenant.</summary>
public interface IAddOnPurchaseIntentRepository
{
    Task AddAsync(AddOnPurchaseIntent intent, CancellationToken ct = default);

    /// <summary>Lectura tenant-scoped (endpoint de estado del tenant).</summary>
    Task<AddOnPurchaseIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default);

    /// <summary>La compra de ESE add-on que el tenant dejó a medias y todavía se puede pagar. A diferencia de
    /// los asientos, dos add-ons distintos sí pueden estar en curso a la vez: son compras independientes.</summary>
    Task<AddOnPurchaseIntent?> GetOpenByTenantAsync(
        Guid tenantId,
        string addOnCode,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>Cross-tenant para el consumer del webhook: ignora el filtro fail-closed; el caller valida el
    /// tenant del evento antes de mutar.</summary>
    Task<AddOnPurchaseIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default);

    /// <summary>Cross-tenant (job de reconciliación): intenciones <c>Pending</c> con pago ya emitido creadas
    /// antes de <paramref name="olderThanUtc"/> — las que pudieron quedar sin activar si se perdió el evento.</summary>
    Task<IReadOnlyList<AddOnPurchaseIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    );
}

/// <summary>Intenciones de renovación/reactivación self-service por hosted-checkout (Expiración/Dunning,
/// Fase 4). Espejo de <see cref="ISeatPurchaseIntentRepository"/>.</summary>
public interface IRenewalCheckoutIntentRepository
{
    Task AddAsync(SubscriptionRenewalIntent intent, CancellationToken ct = default);

    /// <summary>Lectura tenant-scoped (endpoint de estado del tenant).</summary>
    Task<SubscriptionRenewalIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default);

    /// <summary>La renovación que el tenant dejó a medias y todavía se puede pagar. Es el guard contra el
    /// doble cobro al reintentar, igual que en asientos y add-ons.</summary>
    Task<SubscriptionRenewalIntent?> GetOpenByTenantAsync(
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>Cross-tenant para el consumer del webhook: ignora el filtro fail-closed; el caller valida el
    /// tenant del evento antes de mutar.</summary>
    Task<SubscriptionRenewalIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default);

    /// <summary>Cross-tenant (job de reconciliación): intenciones <c>Pending</c> con pago ya emitido creadas
    /// antes de <paramref name="olderThanUtc"/>. Ignora el filtro fail-closed; el job arrastra el TenantId.</summary>
    Task<IReadOnlyList<SubscriptionRenewalIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    );
}

/// <summary>Catálogo GLOBAL singleton de precios de asiento (no por tenant) — el equivalente de
/// <see cref="IAddOnDefinitionRepository"/> para seats. Sembrado al arrancar.</summary>
public interface ISeatPricingRepository
{
    /// <summary>Catálogo con sus tramos, AsNoTracking — para resolver el precio al comprar.</summary>
    Task<SeatPricing?> GetAsync(CancellationToken ct = default);

    /// <summary>Catálogo trackeado (con tramos) para editar precios y persistir.</summary>
    Task<SeatPricing?> GetForUpdateAsync(CancellationToken ct = default);
}

public interface ISubscriptionTenantSettingsRepository
{
    Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default);
}

public interface IAddOnDefinitionRepository
{
    Task<IReadOnlyList<AddOnDefinition>> GetPublishedAsync(CancellationToken ct = default);
    Task<AddOnDefinition?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<AddOnDefinition?> GetByIdAsync(Guid addOnDefinitionId, CancellationToken ct = default);
    Task AddAsync(AddOnDefinition definition, CancellationToken ct = default);
    Task<AddOnDefinition?> GetByIdForUpdateAsync(Guid addOnDefinitionId, CancellationToken ct = default);
}

public interface ITenantAddOnRepository
{
    Task<TenantAddOn?> GetByIdAsync(Guid tenantAddOnId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Add-ons del tenant trackeados (para mutar y persistir, ej. absorción en upgrade).</summary>
    Task<IReadOnlyList<TenantAddOn>> GetByTenantIdForUpdateAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantAddOn addOn, CancellationToken ct = default);

    /// <summary>Batch job queries — cross-tenant by design, only the scheduler calls these.</summary>
    Task<IReadOnlyList<TenantAddOn>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantAddOn>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantAddOn>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantAddOn>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
}

public interface ITenantEntitlementSnapshotRepository
{
    Task<TenantEntitlementSnapshot?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task UpsertAsync(TenantEntitlementSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>RateLimit Fase 6 — catálogo completo, consultado por otros servicios vía
/// GET internal/plan-rate-limits (mismo patrón de exposición M2M de datos
/// globales de Subscription que GetInternalPlanPricingHandler).</summary>
public interface IPlanRateLimitRepository
{
    Task<IReadOnlyList<PlanRateLimit>> GetAllAsync(CancellationToken ct = default);
}
