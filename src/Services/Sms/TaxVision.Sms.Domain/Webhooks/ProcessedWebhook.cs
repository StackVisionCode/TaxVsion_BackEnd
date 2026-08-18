using BuildingBlocks.Domain;

namespace TaxVision.Sms.Domain.Webhooks;

/// <summary>
/// Deduplicación anti-replay de webhooks del proveedor. Único por `(providerCode, providerMessageId, eventType)`.
/// NO es tenant-owned a propósito: el webhook llega anónimo (sin contexto de tenant), así que el chequeo de
/// dedup debe funcionar sin filtro de tenant. <see cref="TenantId"/> es informativo (se resuelve del mensaje).
/// </summary>
public sealed class ProcessedWebhook : BaseEntity
{
    public Guid? TenantId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public string ProviderMessageId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string? PayloadHash { get; private set; }
    public DateTime ProcessedAtUtc { get; private set; }

    private ProcessedWebhook() { }

    public ProcessedWebhook(
        string providerCode,
        string providerMessageId,
        string eventType,
        Guid? tenantId,
        string? payloadHash,
        DateTime nowUtc
    )
    {
        ProviderCode = providerCode;
        ProviderMessageId = providerMessageId;
        EventType = eventType;
        TenantId = tenantId;
        PayloadHash = payloadHash;
        ProcessedAtUtc = nowUtc;
    }
}
