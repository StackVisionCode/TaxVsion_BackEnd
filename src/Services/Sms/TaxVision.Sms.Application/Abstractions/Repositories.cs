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

    /// <summary>STOP sin tenant: cada tenant que le escribió al número, con su último cliente
    /// (cross-tenant, IgnoreQueryFilters).</summary>
    Task<IReadOnlyList<SmsPhoneTenant>> ListTenantsByPhoneAsync(string phoneE164, CancellationToken ct = default);

    /// <summary>Reconciliación por pull: mensajes en <see cref="SmsMessageStatus.Accepted"/> o
    /// <see cref="SmsMessageStatus.OutcomeUnknown"/> con <c>ProviderMessageId</c> (sin DLR final todavía) más
    /// viejos que <paramref name="olderThanUtc"/>. Cross-tenant por default (job de fondo, IgnoreQueryFilters); si se
    /// pasa <paramref name="tenantId"/> se acota a ese tenant (endpoint manual). Orden estable (más viejos
    /// primero) y tope <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SmsMessage>> GetStuckForReconciliationAsync(
        Guid? tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Envíos sin confirmación del proveedor: <c>Pending</c> sin <c>ProviderMessageId</c> reservados antes
    /// de <paramref name="pendingBeforeUtc"/> y <c>OutcomeUnknown</c> reservados antes de <paramref name="unknownBeforeUtc"/>.</summary>
    Task<IReadOnlyList<SmsMessage>> GetUnconfirmedSendsAsync(
        Guid? tenantId,
        DateTime pendingBeforeUtc,
        DateTime unknownBeforeUtc,
        int limit,
        CancellationToken ct = default
    );

    /// <summary>Un mensaje por id dentro del tenant (IgnoreQueryFilters con tenant explícito).</summary>
    Task<SmsMessage?> GetByIdAsync(Guid tenantId, Guid messageId, CancellationToken ct = default);

    /// <summary>Filas de un lote, solo lectura (liquidación del Wallet).</summary>
    Task<IReadOnlyList<SmsMessage>> ListByBatchAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);

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

    /// <summary>Gate de envío: la baja es del número en el tenant, sin importar el cliente.</summary>
    Task<bool> IsPhoneOptedOutAsync(Guid tenantId, string phoneE164, CancellationToken ct = default);

    /// <summary>Todas las filas del número en el tenant (STOP/START y consentimiento manual).</summary>
    Task<IReadOnlyList<SmsOptOut>> ListByTenantAndPhoneAsync(
        Guid tenantId,
        string phoneE164,
        CancellationToken ct = default
    );

    Task AddAsync(SmsOptOut optOut, CancellationToken ct = default);
}

/// <summary>Tenant que le escribió a un número y el cliente de su envío más reciente.</summary>
public sealed record SmsPhoneTenant(Guid TenantId, Guid CustomerId);

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

/// <summary>DLR guardados a la espera de su mensaje. Sin filtro de tenant, igual que la dedup de webhooks.</summary>
public interface IPendingDeliveryReceiptRepository
{
    Task<bool> ExistsAsync(
        string providerCode,
        string providerMessageId,
        string eventType,
        CancellationToken ct = default
    );

    Task AddAsync(PendingDeliveryReceipt receipt, CancellationToken ct = default);

    /// <summary>Los DLR de un mensaje, del más viejo al más nuevo.</summary>
    Task<IReadOnlyList<PendingDeliveryReceipt>> ListAsync(
        string providerCode,
        string providerMessageId,
        CancellationToken ct = default
    );

    void Remove(PendingDeliveryReceipt receipt);

    Task<int> DeleteReceivedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
