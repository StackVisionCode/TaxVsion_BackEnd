using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.Webhooks;

namespace TaxVision.Sms.Application.Abstractions;

public interface ISmsMessageRepository
{
    /// <summary>Idempotencia outbound: busca por `(tenantId, idempotencyKey)`.</summary>
    Task<SmsMessage?> GetByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Webhook (anónimo, sin tenant en contexto): busca por el id del proveedor cross-tenant
    /// (IgnoreQueryFilters en la implementación).</summary>
    Task<SmsMessage?> GetByProviderMessageIdAsync(
        string providerCode,
        string providerMessageId,
        CancellationToken ct = default
    );

    /// <summary>Webhook inbound: resuelve el `(tenant, customer)` real del número por el envío más
    /// reciente hacia él (cross-tenant, IgnoreQueryFilters). Null si nunca se le envió — no se inventa.</summary>
    Task<SmsMessage?> GetLatestByPhoneAsync(string phoneE164, CancellationToken ct = default);

    Task AddAsync(SmsMessage message, CancellationToken ct = default);
}

/// <summary>Resuelve el secreto de verificación de firma del webhook por proveedor (de la config del
/// adapter). Cada servicio es dueño de sus secretos — no hay resolver global.</summary>
public interface ISmsWebhookSecrets
{
    string? GetSecret(string providerCode);
}

public interface ISmsOptOutRepository
{
    /// <summary>Gate de envío: consentimiento por `(tenant, customer, phone)`.</summary>
    Task<SmsOptOut?> GetAsync(Guid tenantId, Guid customerId, string phoneE164, CancellationToken ct = default);

    /// <summary>Webhook inbound: resuelve por `(tenant, phone)` cross-customer (IgnoreQueryFilters).</summary>
    Task<SmsOptOut?> GetByTenantAndPhoneAsync(Guid tenantId, string phoneE164, CancellationToken ct = default);

    Task AddAsync(SmsOptOut optOut, CancellationToken ct = default);
}

public interface IProcessedWebhookRepository
{
    /// <summary>Dedup anti-replay por `(providerCode, providerMessageId, eventType)`.</summary>
    Task<bool> ExistsAsync(
        string providerCode,
        string providerMessageId,
        string eventType,
        CancellationToken ct = default
    );

    Task AddAsync(ProcessedWebhook processed, CancellationToken ct = default);
}
