using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Campaigns.Commands;

// ─────────────────────────── Update (Draft only) ───────────────────────────

/// <summary>Edita una campaña en Draft: nombre, canales y contenido. Fuera de Draft el aggregate rechaza.</summary>
public sealed record UpdateCampaignCommand(
    Guid TenantId,
    Guid CampaignId,
    string Name,
    CampaignChannel Channels,
    string Message,
    string? Subject
);

public static class UpdateCampaignHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        UpdateCampaignCommand command,
        ICampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignResponse>(CampaignErrors.NotFound);

        var rename = campaign.Rename(command.Name);
        if (rename.IsFailure)
            return Result.Failure<CampaignResponse>(rename.Error);
        var channels = campaign.SetChannels(command.Channels);
        if (channels.IsFailure)
            return Result.Failure<CampaignResponse>(channels.Error);
        var content = campaign.SetContent(command.Message, command.Subject);
        if (content.IsFailure)
            return Result.Failure<CampaignResponse>(content.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignResponse.From(campaign));
    }
}

// ─────────────────────────── Mark ready ───────────────────────────

public sealed record MarkCampaignReadyCommand(Guid TenantId, Guid CampaignId);

public static class MarkCampaignReadyHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        MarkCampaignReadyCommand command,
        ICampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignResponse>(CampaignErrors.NotFound);

        var ready = campaign.MarkReady();
        if (ready.IsFailure)
            return Result.Failure<CampaignResponse>(ready.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignResponse.From(campaign));
    }
}

// ─────────────────────────── Delete ───────────────────────────

public sealed record DeleteCampaignCommand(Guid TenantId, Guid CampaignId);

public static class DeleteCampaignHandler
{
    public static async Task<Result> Handle(
        DeleteCampaignCommand command,
        ICampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure(CampaignErrors.NotFound);

        campaigns.Remove(campaign); // selecciones de remitente caen por cascade; runs históricos quedan
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ─────────────────────────── Revert to draft ───────────────────────────

public sealed record RevertCampaignToDraftCommand(Guid TenantId, Guid CampaignId);

public static class RevertCampaignToDraftHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        RevertCampaignToDraftCommand command,
        ICampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignResponse>(CampaignErrors.NotFound);

        var reverted = campaign.RevertToDraft();
        if (reverted.IsFailure)
            return Result.Failure<CampaignResponse>(reverted.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignResponse.From(campaign));
    }
}

// ─────────────────────────── Archive ───────────────────────────

public sealed record ArchiveCampaignCommand(Guid TenantId, Guid CampaignId);

public static class ArchiveCampaignHandler
{
    public static async Task<Result<CampaignResponse>> Handle(
        ArchiveCampaignCommand command,
        ICampaignRepository campaigns,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignResponse>(CampaignErrors.NotFound);

        var archived = campaign.Archive();
        if (archived.IsFailure)
            return Result.Failure<CampaignResponse>(archived.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignResponse.From(campaign));
    }
}
