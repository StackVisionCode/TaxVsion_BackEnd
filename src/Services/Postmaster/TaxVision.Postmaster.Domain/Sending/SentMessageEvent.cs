using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Sending;

public enum SentMessageEventType
{
    Queued,
    Sent,
    Failed,
    Suppressed,
    Retry,
    Delivered,
    Deferred,
    Bounced,
    Rejected,
    Complained,
    Opened,
    Clicked,
    Unsubscribed,

    /// <summary>Falló el buzón de la oficina y se reintentó por el proveedor del sistema.</summary>
    Fallback,
}

/// <summary>
/// Historial append-only de un <see cref="SentMessage"/>: sus transiciones y, por destinatario, cada
/// aviso de entrega que llegó (aunque haya llegado tarde y no cambie el estado).
/// </summary>
public sealed class SentMessageEvent : TenantEntity
{
    private SentMessageEvent() { }

    public Guid SentMessageId { get; private set; }
    public Guid? RecipientId { get; private set; }
    public SentMessageEventType EventType { get; private set; }
    public DateTime EventAtUtc { get; private set; }

    /// <summary>Payload crudo del proveedor, solo para auditoría. Nunca se interpreta fuera de su adaptador.</summary>
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
