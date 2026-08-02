namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition PaymentAppWebhookStripe = Define(
        "payment_app.e.webhook_stripe",
        RateLimitCategory.E,
        RateLimitPartitionDimension.Ip,
        [],
        quota: 1000,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition PaymentAppSaaSPaymentRead = Define(
        "payment_app.f.saas_payment_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition PaymentAppProviderCustomerRead = Define(
        "payment_app.f.provider_customer_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por CreateSetupIntent + AttachMethod + DetachMethod (TenantProviderCustomersController)
    // — las 3 llaman síncronamente al proveedor (Stripe) dentro del propio request, mismo criterio
    // que correspondence.l.draft_send. SetDefaultMethod NO llama al proveedor (solo reordena el flag
    // local), queda en payment_app.g.provider_customer_manage.
    public static readonly RateLimitPolicyDefinition PaymentAppProviderCustomerWrite = Define(
        "payment_app.l.provider_customer_write",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    public static readonly RateLimitPolicyDefinition PaymentAppProviderCustomerManage = Define(
        "payment_app.g.provider_customer_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por SearchAllTenants + SearchForTenant (PaymentAppAdminController) — ambas
    // delegan al mismo método privado Search, único filtro distinto es tenantId opcional.
    public static readonly RateLimitPolicyDefinition PaymentAppAdminRead = Define(
        "payment_app.f.admin_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition PaymentAppAdminManage = Define(
        "payment_app.g.admin_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Export CSV capado a 5000 filas sin paginación — mismo perfil que customer.h.imports_report.
    public static readonly RateLimitPolicyDefinition PaymentAppAdminExport = Define(
        "payment_app.h.admin_export",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    public static readonly RateLimitPolicyDefinition PaymentAppCheckoutCreate = Define(
        "payment_app.l.checkout_create",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    public static readonly RateLimitPolicyDefinition PaymentAppRefund = Define(
        "payment_app.m.refund",
        RateLimitCategory.M,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 5,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );
}
