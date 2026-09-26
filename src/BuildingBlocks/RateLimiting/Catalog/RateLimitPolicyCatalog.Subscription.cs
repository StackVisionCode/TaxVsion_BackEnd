namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Auditoría post-Fase-9 (hallazgo #10) — AuditController.Search acepta filtros reales
    // (aggregateType, aggregateId, rango de fechas) + paginación: búsqueda pesada (H), no lectura
    // simple (F). Era F desde Fase 4.10 por descuido de clasificación, nunca por falta de filtros.
    public static readonly RateLimitPolicyDefinition SubscriptionAuditRead = Define(
        "subscription.h.audit_read",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionEntitlementRead = Define(
        "subscription.f.entitlement_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAddOnRead = Define(
        "subscription.f.addon_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionSeatRead = Define(
        "subscription.f.seat_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionSubscriptionRead = Define(
        "subscription.f.subscription_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAdminRead = Define(
        "subscription.f.admin_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAddOnManage = Define(
        "subscription.g.addon_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Iniciar el cobro de un add-on: mueve dinero, así que va en L como el de asientos. Comprar off-session
    // (POST /addons) sigue en g.addon_manage junto con cancelar.
    public static readonly RateLimitPolicyDefinition SubscriptionAddOnPurchase = Define(
        "subscription.l.addon_purchase",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    // Iniciar un cobro de asientos (checkout hosteado o cargo con método en archivo): categoría L, el mismo
    // perfil y cupo que payment_app.l.checkout_create. Antes compartía g.seat_manage con asignar/liberar,
    // que no mueven dinero.
    public static readonly RateLimitPolicyDefinition SubscriptionSeatPurchase = Define(
        "subscription.l.seat_purchase",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    public static readonly RateLimitPolicyDefinition SubscriptionSeatManage = Define(
        "subscription.g.seat_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Renovar a mano una suscripción vencida arranca un cobro: L, como el resto de las compras. Antes
    // compartía la política de gestión, que es mucho más ancha.
    public static readonly RateLimitPolicyDefinition SubscriptionRenewCheckout = Define(
        "subscription.l.renew_checkout",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    // Pedir un cambio de plan: un upgrade arranca un cobro, así que va en L como asientos y add-ons.
    // Consultar el cambio pendiente o cancelarlo no mueve dinero y se queda en g.plan_change.
    public static readonly RateLimitPolicyDefinition SubscriptionPlanChangeRequest = Define(
        "subscription.l.plan_change",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    public static readonly RateLimitPolicyDefinition SubscriptionPlanChange = Define(
        "subscription.g.plan_change",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionActivate = Define(
        "subscription.g.subscription_activate",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionManage = Define(
        "subscription.g.subscription_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAdminManage = Define(
        "subscription.g.admin_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
