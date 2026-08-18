namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition ConnectorsWebhookGmailPush = Define(
        "connectors.e.webhook_gmail_push",
        RateLimitCategory.E,
        RateLimitPartitionDimension.Ip,
        [],
        quota: 1000,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    // Compartida por List + GetById (AccountsController).
    public static readonly RateLimitPolicyDefinition ConnectorsAccountsRead = Define(
        "connectors.f.accounts_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Initiate + Disconnect + AdminConsentUrl + Reauth (AccountsController) —
    // escritura simple de una fila o generación de una URL de consentimiento, sin I/O externo caro.
    public static readonly RateLimitPolicyDefinition ConnectorsAccountsManage = Define(
        "connectors.g.accounts_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // ConnectManual — a diferencia del resto, valida conectividad real contra servidores IMAP+SMTP
    // externos de forma síncrona dentro del propio request, mismo perfil caro que
    // signature.i.document_validate.
    public static readonly RateLimitPolicyDefinition ConnectorsAccountsManualConnect = Define(
        "connectors.i.accounts_manual_connect",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 5,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 20
    );

    public static readonly RateLimitPolicyDefinition ConnectorsSend = Define(
        "connectors.k.send",
        RateLimitCategory.K,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.AccountOrProvider,
        [RateLimitPartitionDimension.AccountOrProvider],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.LeakyBucket
    );
}
