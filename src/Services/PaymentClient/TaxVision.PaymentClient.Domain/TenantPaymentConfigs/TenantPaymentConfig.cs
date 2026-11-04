using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

/// <summary>
/// Configuración de un payment provider para un tenant específico — a diferencia de
/// PaymentApp (credenciales globales de plataforma), acá cada tenant trae sus propias
/// credenciales. Único por <c>(TenantId, ProviderCode)</c> — un tenant puede configurar
/// varios providers y dejar que el taxpayer elija con cuál pagar (§44 del diseño:
/// "absolutamente cualquier sistema de pago").
///
/// Nace <see cref="IsActive"/> = false: solo <see cref="MarkActive"/> lo habilita, y eso
/// requiere que los secretos ya estén cargados — no hay forma de que un config a medio
/// llenar empiece a aceptar cobros.
/// </summary>
public sealed class TenantPaymentConfig : TenantEntity
{
    private readonly List<TenantWebhookEndpoint> _webhookEndpoints = [];

    public PaymentProviderCode ProviderCode { get; private set; }
    public TenantPaymentMode Mode { get; private set; }
    public string PublishableKey { get; private set; } = default!;

    /// <summary>URL/endpoint del proveedor, configurable por el tenant. Opcional — Stripe no lo
    /// necesita (SDK con endpoints fijos), pero otros gateways (IntelliPay, un procesador propio, etc.)
    /// requieren su URL base. Null = usar el default del adapter.</summary>
    public string? ApiBaseUrl { get; private set; }
    public EncryptedSecret? SecretKeyEncrypted { get; private set; }
    public EncryptedSecret? WebhookSecretEncrypted { get; private set; }

    /// <summary>Estado del webhook en el proveedor (null = nunca intentado y sin manual).</summary>
    public WebhookSetupStatus? WebhookSetup { get; private set; }

    /// <summary>Id del webhook que creó el backend en el proveedor (para reemplazarlo o borrarlo). Null si
    /// nunca se creó uno automático.</summary>
    public string? ProviderWebhookId { get; private set; }

    /// <summary>Cuándo se encoló (o re-encoló) el registro automático; la revisión de pendientes lo usa
    /// para detectar uno que se perdió (caída entre guardar y encolar).</summary>
    public DateTime? WebhookSetupRequestedAtUtc { get; private set; }
    public StatementDescriptor StatementDescriptor { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime? SettledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<TenantWebhookEndpoint> WebhookEndpoints => _webhookEndpoints;

    private TenantPaymentConfig() { }

    public static Result<TenantPaymentConfig> Create(
        Guid tenantId,
        PaymentProviderCode providerCode,
        TenantPaymentMode mode,
        string publishableKey,
        StatementDescriptor descriptor,
        DateTime nowUtc,
        string? apiBaseUrl = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantPaymentConfig>(
                new Error("TenantPaymentConfig.InvalidTenant", "TenantId is required.")
            );

        if (string.IsNullOrWhiteSpace(publishableKey))
            return Result.Failure<TenantPaymentConfig>(
                new Error("TenantPaymentConfig.InvalidPublishableKey", "PublishableKey is required.")
            );

        var config = new TenantPaymentConfig
        {
            ProviderCode = providerCode,
            Mode = mode,
            PublishableKey = publishableKey.Trim(),
            ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl.Trim(),
            StatementDescriptor = descriptor,
            IsActive = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        config.SetTenant(tenantId);
        return Result.Success(config);
    }

    /// <summary>Edita la URL/endpoint del proveedor (settings del tenant). Null/whitespace la limpia.</summary>
    public Result UpdateApiBaseUrl(string? apiBaseUrl, Guid actorUserId, DateTime nowUtc)
    {
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl.Trim();
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>El webhook secret es opcional: cobrar no depende de él, solo sincroniza reembolsos y
    /// disputas. Null = no se envió y se conserva el que ya hubiera guardado.</summary>
    public Result UpdateSecrets(
        EncryptedSecret secretKey,
        EncryptedSecret? webhookSecret,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Mode != TenantPaymentMode.DirectApiKeys)
            return Result.Failure(
                new Error(
                    "TenantPaymentConfig.WrongMode",
                    "Secrets only apply to DirectApiKeys mode; a Connect config activates via the Connect account."
                )
            );

        SecretKeyEncrypted = secretKey;
        if (webhookSecret is not null)
        {
            WebhookSecretEncrypted = webhookSecret;
            WebhookSetup = WebhookSetupStatus.Manual;
        }
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RotateWebhookSecret(EncryptedSecret newSecret, Guid actorUserId, DateTime nowUtc)
    {
        WebhookSecretEncrypted = newSecret;
        WebhookSetup = WebhookSetupStatus.Manual;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Encola el registro automático del webhook. Nunca pisa un secreto manual.</summary>
    public Result BeginAutomaticWebhookSetup(DateTime nowUtc)
    {
        if (Mode != TenantPaymentMode.DirectApiKeys)
            return Result.Failure(
                new Error("TenantPaymentConfig.WrongMode", "Webhooks are only registered for DirectApiKeys mode.")
            );

        if (WebhookSetup == WebhookSetupStatus.Manual)
            return Result.Failure(
                new Error("TenantPaymentConfig.ManualWebhook", "The office configured its webhook manually.")
            );

        WebhookSetup = WebhookSetupStatus.Pending;
        WebhookSetupRequestedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Re-encolado de un pendiente que se perdió: renueva la marca para no dispararlo dos veces.</summary>
    public Result RenewWebhookSetupRequest(DateTime nowUtc)
    {
        if (WebhookSetup != WebhookSetupStatus.Pending)
            return Result.Failure(
                new Error("TenantPaymentConfig.WebhookSetupNotPending", "The automatic webhook setup is not pending.")
            );

        WebhookSetupRequestedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Guarda el secreto del webhook que creó el backend. Solo si sigue pendiente: si mientras
    /// tanto llegó uno manual, gana el manual.</summary>
    public Result CompleteAutomaticWebhookSetup(
        EncryptedSecret webhookSecret,
        string providerWebhookId,
        DateTime nowUtc
    )
    {
        if (WebhookSetup != WebhookSetupStatus.Pending)
            return Result.Failure(
                new Error("TenantPaymentConfig.WebhookSetupNotPending", "The automatic webhook setup is not pending.")
            );

        if (string.IsNullOrWhiteSpace(providerWebhookId))
            return Result.Failure(
                new Error("TenantPaymentConfig.InvalidProviderWebhookId", "The provider webhook id is required.")
            );

        WebhookSecretEncrypted = webhookSecret;
        ProviderWebhookId = providerWebhookId.Trim();
        WebhookSetup = WebhookSetupStatus.Automatic;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result FailAutomaticWebhookSetup(DateTime nowUtc)
    {
        if (WebhookSetup != WebhookSetupStatus.Pending)
            return Result.Failure(
                new Error("TenantPaymentConfig.WebhookSetupNotPending", "The automatic webhook setup is not pending.")
            );

        WebhookSetup = WebhookSetupStatus.Failed;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>El webhook automático ya se borró en el proveedor: deja de recordarlo.</summary>
    public void ForgetProviderWebhook(string providerWebhookId)
    {
        if (string.Equals(ProviderWebhookId, providerWebhookId, StringComparison.Ordinal))
            ProviderWebhookId = null;
    }

    public Result UpdateStatementDescriptor(StatementDescriptor descriptor, Guid actorUserId, DateTime nowUtc)
    {
        StatementDescriptor = descriptor;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Basta la clave secreta: sin webhook secret se cobra igual, pero los webhooks de esa
    /// oficina se rechazan por no poder verificarse.</summary>
    public Result MarkActive(Guid actorUserId, DateTime nowUtc)
    {
        if (Mode != TenantPaymentMode.DirectApiKeys)
            return Result.Failure(
                new Error("TenantPaymentConfig.WrongMode", "Use MarkActiveViaConnect for a Connect-mode config.")
            );

        if (SecretKeyEncrypted is null)
            return Result.Failure(
                new Error("TenantPaymentConfig.SecretsMissing", "Cannot activate a config with no secrets loaded.")
            );

        IsActive = true;
        SettledAtUtc ??= nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Contraparte de <see cref="MarkActive"/> para <see cref="TenantPaymentMode.Connect"/>
    /// — no hay secretos que cargar; la activación la dispara el webhook
    /// <c>account.updated</c> cuando <c>TenantConnectAccount.CanCharge</c> pasa a true (§21.2.3
    /// del diseño: sin eso, cualquier intento de cobro falla con <c>Connect.NotChargeReady</c>
    /// antes de siquiera llegar acá).</summary>
    public Result MarkActiveViaConnect(Guid actorUserId, DateTime nowUtc)
    {
        if (Mode != TenantPaymentMode.Connect)
            return Result.Failure(
                new Error("TenantPaymentConfig.WrongMode", "Use MarkActive for a DirectApiKeys-mode config.")
            );

        IsActive = true;
        SettledAtUtc ??= nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Deactivate(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantPaymentConfig.InvalidReason", "Reason is required."));

        IsActive = false;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result<Guid> AddWebhookEndpoint(string url, EncryptedSecret signingSecret, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Result.Failure<Guid>(new Error("TenantPaymentConfig.InvalidWebhookUrl", "Url is required."));

        var endpoint = TenantWebhookEndpoint.Create(Id, TenantId, url.Trim(), signingSecret, nowUtc);
        _webhookEndpoints.Add(endpoint);
        return Result.Success(endpoint.Id);
    }

    public Result DeactivateWebhookEndpoint(Guid webhookEndpointId)
    {
        foreach (var endpoint in _webhookEndpoints)
        {
            if (endpoint.Id == webhookEndpointId)
            {
                endpoint.Deactivate();
                return Result.Success();
            }
        }

        return Result.Failure(
            new Error("TenantPaymentConfig.WebhookEndpointNotFound", "Webhook endpoint does not exist.")
        );
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
