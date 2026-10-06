using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Api.Requests;

/// <summary>Body de crear/editar una plantilla. Enums como string (JsonStringEnumConverter global).
/// Reusa <see cref="CampaignContentRequest"/> (de CampaignRequests) para el contenido por canal.</summary>
public sealed record CampaignTemplateRequest(
    string Name,
    string? Description,
    IReadOnlyList<CampaignChannel> Channels,
    IReadOnlyList<CampaignContentRequest>? Contents = null
)
{
    public CampaignChannel ToChannelsFlag() =>
        Channels is null ? CampaignChannel.None : Channels.Aggregate(CampaignChannel.None, (acc, c) => acc | c);
}
