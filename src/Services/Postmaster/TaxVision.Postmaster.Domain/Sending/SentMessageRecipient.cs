using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Sending;

public enum RecipientType
{
    To,
    Cc,
    Bcc,
}

public enum RecipientStatus
{
    Pending,
    Sent,
    Failed,
    Suppressed,
}

/// <summary>
/// Destinatario de un <see cref="SentMessage"/>. Solo se crea vía <c>SentMessage.AddRecipient</c> —
/// el constructor y <see cref="Create"/> son internos al bounded context de Sending.
/// </summary>
public sealed class SentMessageRecipient : TenantEntity
{
    private SentMessageRecipient() { }

    public Guid SentMessageId { get; private set; }
    public string Address { get; private set; } = default!;
    public string? DisplayName { get; private set; }
    public RecipientType Type { get; private set; }
    public RecipientStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTime? LastEventAtUtc { get; private set; }
    public string? ErrorReason { get; private set; }

    internal static SentMessageRecipient Create(
        Guid tenantId,
        Guid sentMessageId,
        string normalizedAddress,
        RecipientType type,
        string? displayName
    )
    {
        var recipient = new SentMessageRecipient
        {
            Id = Guid.NewGuid(),
            SentMessageId = sentMessageId,
            Address = normalizedAddress,
            DisplayName = displayName is { Length: > 200 } ? displayName[..200] : displayName,
            Type = type,
            Status = RecipientStatus.Pending,
        };
        recipient.SetTenant(tenantId);
        return recipient;
    }

    internal void MarkAsSent(string? providerMessageId)
    {
        Status = RecipientStatus.Sent;
        if (providerMessageId is not null)
            ProviderMessageId = providerMessageId.Length > 200 ? providerMessageId[..200] : providerMessageId;
    }

    /// <summary>
    /// Aplica un evento al recipient (invocado solo por el aggregate padre). En la práctica hoy el único
    /// productor real es la supresión pre-envío — ver <c>SentMessage.RecordDeliveryEvent</c>.
    /// </summary>
    internal void ApplyEvent(SentMessageEventType eventType, string? reason)
    {
        Status = eventType switch
        {
            SentMessageEventType.Failed => RecipientStatus.Failed,
            SentMessageEventType.Suppressed => RecipientStatus.Suppressed,
            // Retry/Queued/Sent no cambian el status del recipient — ya lo cubre MarkAsSent.
            _ => Status,
        };
        if (reason is not null)
            ErrorReason = reason.Length > 500 ? reason[..500] : reason;
    }
}
