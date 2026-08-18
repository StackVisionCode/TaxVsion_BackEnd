namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition PaymentClientPayoutRead = Define(
        "payment_client.f.payout_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition PaymentClientPayoutManage = Define(
        "payment_client.g.payout_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Onboard llama gateway.CreateAccountAsync/CreateOnboardingLinkAsync síncronamente contra
    // Stripe dentro del propio request (InitiateStripeConnectOnboardingHandler) — mismo criterio
    // L que payment_app.l.provider_customer_write.
    public static readonly RateLimitPolicyDefinition PaymentClientConnectOnboard = Define(
        "payment_client.l.connect_onboard",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    public static readonly RateLimitPolicyDefinition PaymentClientConnectRead = Define(
        "payment_client.f.connect_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create + Revoke (PaymentLinksController) — CreatePaymentLinkHandler solo
    // genera un token local (PaymentLinkToken.Generate()), Stripe nunca se contacta al crear el
    // link, así que queda en G y no en L.
    public static readonly RateLimitPolicyDefinition PaymentClientPaymentLinkManage = Define(
        "payment_client.g.payment_link_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition PaymentClientPaymentLinkRead = Define(
        "payment_client.f.payment_link_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Charge llama adapter.AuthorizeChargeAsync síncronamente (ChargeTenantPaymentHandler, tanto
    // en el camino Direct como en el Connect) — mismo criterio L que
    // payment_client.l.connect_onboard, política propia porque vive en otro controller.
    public static readonly RateLimitPolicyDefinition PaymentClientPaymentCharge = Define(
        "payment_client.l.payment_charge",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    public static readonly RateLimitPolicyDefinition PaymentClientPaymentRead = Define(
        "payment_client.f.payment_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por SearchAllTenants + SearchForTenant (PaymentClientAdminController) — mismo
    // patrón que payment_app.f.admin_read.
    public static readonly RateLimitPolicyDefinition PaymentClientAdminRead = Define(
        "payment_client.f.admin_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Export CSV capado a 5000 filas sin paginación — mismo perfil que payment_app.h.admin_export.
    public static readonly RateLimitPolicyDefinition PaymentClientAdminExport = Define(
        "payment_client.h.admin_export",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    // Compartida por Create/Pause/Resume/Cancel (TenantRecurringPaymentsController) — los 4
    // handlers son puramente locales, el cobro real ocurre después en
    // ExecuteRecurringScheduleHandler (fuera del alcance HTTP de esta fase).
    public static readonly RateLimitPolicyDefinition PaymentClientRecurringManage = Define(
        "payment_client.g.recurring_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Search + Get (TenantRecurringPaymentsController).
    public static readonly RateLimitPolicyDefinition PaymentClientRecurringRead = Define(
        "payment_client.f.recurring_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por List + Get (TenantPaymentConfigsController).
    public static readonly RateLimitPolicyDefinition PaymentClientConfigRead = Define(
        "payment_client.f.config_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create/UpdateSecrets/Deactivate/Activate (TenantPaymentConfigsController) —
    // los 4 solo cifran/guardan config local, sin llamada síncrona al proveedor.
    public static readonly RateLimitPolicyDefinition PaymentClientConfigManage = Define(
        "payment_client.g.config_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // M2M — Billing pidiendo asegurar (find-or-create) el payable de una factura
    // (InternalPayablesController.EnsureInvoice). El JWT de servicio SÍ trae TenantId
    // (JwtTokenGenerator.GenerateScopedServiceToken lo setea siempre) — la exención previa
    // asumía lo contrario. Mismo shape que documents.j.invoice_generate (categoría J, M2M
    // particionado solo por Tenant, auditoría independiente lo corrigió).
    public static readonly RateLimitPolicyDefinition PaymentClientEnsureInvoice = Define(
        "payment_client.j.ensure_invoice",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );
}
