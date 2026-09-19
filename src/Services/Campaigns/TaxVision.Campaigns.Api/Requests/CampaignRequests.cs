using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Api.Requests;

/// <summary>DTOs de body. Enums serializados como string (JsonStringEnumConverter global).</summary>
public sealed record CreateCampaignRequest(
    string Name,
    IReadOnlyList<CampaignChannel> Channels,
    string Message,
    string? Subject
)
{
    /// <summary>Combina la lista de canales del body en el flag agregado del dominio.</summary>
    public CampaignChannel ToChannelsFlag() =>
        Channels is null ? CampaignChannel.None : Channels.Aggregate(CampaignChannel.None, (acc, c) => acc | c);
}

/// <summary>Envío inmediato — audiencia manual (slice 2). Cada persona se expande a una unidad por canal.</summary>
public sealed record SendNowRequest(IReadOnlyList<SendNowRecipient> Recipients);

public sealed record SendNowRecipient(string ContactRef, string? Email, string? PhoneE164);
