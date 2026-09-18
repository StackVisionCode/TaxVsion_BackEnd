using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Campaigns.Commands;

/// <summary>
/// Crea una campaña en estado <c>Draft</c>. La identidad (<paramref name="TenantId"/>,
/// <paramref name="CreatedByUserId"/>) viene del JWT en el controller, nunca del body.
/// </summary>
public sealed record CreateCampaignCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Name,
    CampaignChannel Channels,
    string Message,
    string? Subject
);

public static class CreateCampaignHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        CreateCampaignCommand command,
        ICampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaignResult = Campaign.Create(
            command.TenantId,
            command.CreatedByUserId,
            command.Name,
            command.Channels,
            command.Message,
            command.Subject
        );
        if (campaignResult.IsFailure)
            return Result.Failure<CampaignResponse>(campaignResult.Error);

        var campaign = campaignResult.Value;
        await campaigns.AddAsync(campaign, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(CampaignResponse.From(campaign));
    }
}
