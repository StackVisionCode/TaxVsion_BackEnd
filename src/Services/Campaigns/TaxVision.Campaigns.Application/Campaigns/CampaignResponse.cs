using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Campaigns;

/// <summary>DTO de salida de una campaña — nunca se devuelve el aggregate al Api.</summary>
public sealed record CampaignSenderSelectionResponse(string Channel, Guid SenderProfileId);

public sealed record CampaignResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    Guid CreatedByUserId,
    IReadOnlyList<string> Channels,
    string? Subject,
    string Message,
    string Status,
    IReadOnlyList<CampaignSenderSelectionResponse> Senders,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static CampaignResponse From(Campaign campaign) =>
        new(
            campaign.Id,
            campaign.TenantId,
            campaign.Name,
            campaign.CreatedByUserId,
            SplitChannels(campaign.Channels),
            campaign.Subject,
            campaign.Message,
            campaign.Status.ToString(),
            campaign
                .Senders.Select(s => new CampaignSenderSelectionResponse(s.Channel.ToString(), s.SenderProfileId))
                .ToList(),
            campaign.CreatedAtUtc,
            campaign.UpdatedAtUtc
        );

    private static IReadOnlyList<string> SplitChannels(CampaignChannel channels) =>
        Enum.GetValues<CampaignChannel>()
            .Where(c => c != CampaignChannel.None && channels.HasFlag(c))
            .Select(c => c.ToString())
            .ToList();
}
