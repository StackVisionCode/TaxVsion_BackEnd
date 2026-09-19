using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Application.Campaigns.Commands;

/// <summary>
/// Selecciona el remitente (<c>SenderProfile</c>) de una campaña para un canal. Valida que el perfil
/// exista, sea del tenant, esté <c>Active</c> y sea de ese mismo canal; la regla "solo en Draft / canal
/// de la campaña" la impone el aggregate.
/// </summary>
public sealed record SetCampaignSenderCommand(
    Guid TenantId,
    Guid CampaignId,
    CampaignChannel Channel,
    Guid SenderProfileId
);

public static class SetCampaignSenderHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        SetCampaignSenderCommand command,
        ICampaignRepository campaigns,
        ISenderProfileRepository senders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignResponse>(CampaignErrors.NotFound);

        var sender = await senders.GetByIdAsync(command.TenantId, command.SenderProfileId, ct);
        if (sender is null)
            return Result.Failure<CampaignResponse>(SenderProfileErrors.NotFound);
        if (!sender.IsActive)
            return Result.Failure<CampaignResponse>(SenderProfileErrors.NotActive);
        if (sender.Channel != command.Channel)
            return Result.Failure<CampaignResponse>(SenderProfileErrors.ChannelMismatch);

        var set = campaign.SetSender(command.Channel, command.SenderProfileId);
        if (set.IsFailure)
            return Result.Failure<CampaignResponse>(set.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignResponse.From(campaign));
    }
}
