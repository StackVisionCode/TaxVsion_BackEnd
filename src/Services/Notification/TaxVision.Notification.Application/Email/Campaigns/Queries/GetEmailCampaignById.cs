using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Campaigns.Queries;

public sealed record GetEmailCampaignByIdQuery(Guid CampaignId, Guid TenantId);

public static class GetEmailCampaignByIdHandler
{
    public static async Task<Result<EmailCampaignResponse>> Handle(
        GetEmailCampaignByIdQuery query,
        IEmailCampaignRepository repository,
        CancellationToken ct
    )
    {
        var campaign = await repository.GetByIdAsync(query.CampaignId, query.TenantId, ct);
        return campaign is null
            ? Result.Failure<EmailCampaignResponse>(new Error("Campaign.NotFound", "Campaign not found."))
            : Result.Success(EmailCampaignMapper.ToResponse(campaign));
    }
}
