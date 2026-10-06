using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Api.Requests;

/// <summary>DTOs de body. Enums serializados como string (JsonStringEnumConverter global).</summary>
public sealed record CreateCampaignRequest(
    string Name,
    IReadOnlyList<CampaignChannel> Channels,
    string Message,
    string? Subject,
    IReadOnlyList<CampaignContentRequest>? Contents = null
)
{
    /// <summary>Combina la lista de canales del body en el flag agregado del dominio.</summary>
    public CampaignChannel ToChannelsFlag() =>
        Channels is null ? CampaignChannel.None : Channels.Aggregate(CampaignChannel.None, (acc, c) => acc | c);
}

/// <summary>Contenido por canal en el body (Email: Subject+Body · Push: Title+Body · SMS: Body).</summary>
public sealed record CampaignContentRequest(CampaignChannel Channel, string? Subject, string? Title, string Body);

/// <summary>Envío inmediato — audiencia manual (slice 2). Cada persona se expande a una unidad por canal.</summary>
public sealed record SendNowRequest(IReadOnlyList<SendNowRecipient> Recipients);

public sealed record SendNowRecipient(string ContactRef, string? Email, string? PhoneE164);
