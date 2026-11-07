using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Campaigns;

public enum CampaignDispatchSendStatus
{
    /// <summary>Se va a llamar al proveedor, o ya se llamó y el resultado no quedó registrado.</summary>
    Reserved,
    Accepted,
    Failed,
}

/// <summary>
/// Un envío de campaña por <c>DispatchId</c> (NOT-09). El inbox deduplica por id de mensaje: si el mismo DispatchId
/// llega en otro sobre, esta fila impide el segundo envío. <see cref="CampaignDispatchSendStatus.Reserved"/> sin
/// resultado nunca se reenvía.
/// </summary>
public sealed class CampaignDispatchSend : TenantEntity
{
    public const int DispatchIdMaxLength = 80;
    public const int ProviderMessageIdMaxLength = 200;
    public const int FailureReasonMaxLength = 200;

    private CampaignDispatchSend() { }

    public string DispatchId { get; private set; } = default!;
    public CampaignDispatchSendStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static CampaignDispatchSend Reserve(Guid tenantId, string dispatchId, DateTime nowUtc)
    {
        var send = new CampaignDispatchSend
        {
            DispatchId = dispatchId,
            Status = CampaignDispatchSendStatus.Reserved,
            CreatedAtUtc = nowUtc,
        };
        send.SetTenant(tenantId);
        return send;
    }

    public void MarkAccepted(string? providerMessageId, DateTime nowUtc)
    {
        Status = CampaignDispatchSendStatus.Accepted;
        ProviderMessageId = Fit(providerMessageId, ProviderMessageIdMaxLength);
        CompletedAtUtc = nowUtc;
    }

    public void MarkFailed(string? reason, DateTime nowUtc)
    {
        Status = CampaignDispatchSendStatus.Failed;
        FailureReason = Fit(reason, FailureReasonMaxLength);
        CompletedAtUtc = nowUtc;
    }

    private static string? Fit(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}
