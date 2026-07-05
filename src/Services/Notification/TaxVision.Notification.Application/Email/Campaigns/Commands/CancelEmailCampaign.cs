using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Campaigns.Commands;

public sealed record CancelEmailCampaignCommand(Guid CampaignId, Guid TenantId);

public static class CancelEmailCampaignHandler
{
    public static async Task<Result> Handle(
        CancelEmailCampaignCommand command,
        IEmailCampaignRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await repository.GetByIdAsync(command.CampaignId, command.TenantId, ct);
        if (campaign is null)
            return Result.Failure(new Error("Campaign.NotFound", "Campaign not found."));

        var result = campaign.Cancel();
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
