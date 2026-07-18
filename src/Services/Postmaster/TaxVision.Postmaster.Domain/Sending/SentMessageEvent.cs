using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Sending;

public enum SentMessageEventType
{
    Queued,
    Sent,
    Failed,
    Suppressed,
    Retry,
}

/// <summary>
/// Log append-only de eventos del ciclo de vida de un <see cref="SentMessage"/>. Solo se crea vía
/// <c>SentMessage</c> — refleja transiciones internas (Queued, Sent, Failed, Suppressed) y, a nivel
/// de recipient individual, supresiones aplicadas antes del envío (ver <c>SentMessage.RecordDeliveryEvent</c>).
/// Delivered/Bounced/Opened/Clicked/Complained existieron durante el tracking de entrega por webhook
/// que se evaluó y retiró (over-engineering sin caller real) — no quedan en este enum; si se decide
/// reconstruir tracking de entrega real, se reintroducen ahí con sus productores reales.
/// </summary>
public sealed class SentMessageEvent : TenantEntity
{
    private SentMessageEvent() { }

    public Guid SentMessageId { get; private set; }
    public Guid? RecipientId { get; private set; }
    public SentMessageEventType EventType { get; private set; }
    public DateTime EventAtUtc { get; private set; }
    public string? RawPayload { get; private set; }
    public string? Reason { get; private set; }

    internal static SentMessageEvent Create(
        Guid tenantId,
        Guid sentMessageId,
        Guid? recipientId,
        SentMessageEventType eventType,
        DateTime eventAtUtc,
        string? rawPayload,
        string? reason
    )
    {
        var evt = new SentMessageEvent
        {
            Id = Guid.NewGuid(),
            SentMessageId = sentMessageId,
            RecipientId = recipientId,
            EventType = eventType,
            EventAtUtc = eventAtUtc,
            RawPayload = rawPayload is { Length: > 8192 } ? rawPayload[..8192] : rawPayload,
            Reason = reason is { Length: > 500 } ? reason[..500] : reason,
        };
        evt.SetTenant(tenantId);
        return evt;
    }
}
