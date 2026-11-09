using BuildingBlocks.Domain;

namespace TaxVision.Sms.Domain.Webhooks;

/// <summary>
/// DLR que llegó antes de que el envío registrara su <see cref="ProviderMessageId"/>. Se aplica al aceptar el mensaje
/// y se borra. Aviso técnico, no dato de negocio: lo que nadie reclama se limpia por antigüedad. No es tenant-owned:
/// el webhook llega anónimo y sin mensaje no hay tenant.
/// </summary>
public sealed class PendingDeliveryReceipt : BaseEntity
{
    public string ProviderCode { get; private set; } = default!;
    public string ProviderMessageId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string CanonicalStatus { get; private set; } = default!;
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    private PendingDeliveryReceipt() { }

    public PendingDeliveryReceipt(
        string providerCode,
        string providerMessageId,
        string eventType,
        string canonicalStatus,
        string? failureCode,
        string? failureReason,
        DateTime receivedAtUtc
    )
    {
        ProviderCode = providerCode;
        ProviderMessageId = providerMessageId;
        EventType = eventType;
        CanonicalStatus = canonicalStatus;
        FailureCode = failureCode;
        FailureReason = failureReason;
        ReceivedAtUtc = receivedAtUtc;
    }
}
