using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Application.Senders.Commands;

// ─────────────────────────── Create ───────────────────────────

public sealed record CreateSenderProfileCommand(Guid TenantId, CampaignChannel Channel, string Name, string SenderRef);

public static class CreateSenderProfileHandler
{
    public static async Task<Result<SenderProfileResponse>> Handle(
        CreateSenderProfileCommand command,
        ISenderProfileRepository senders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var result = SenderProfile.Create(command.TenantId, command.Channel, command.Name, command.SenderRef);
        if (result.IsFailure)
            return Result.Failure<SenderProfileResponse>(result.Error);

        await senders.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(SenderProfileResponse.From(result.Value));
    }
}

// ─────────────────────────── Update ───────────────────────────

public sealed record UpdateSenderProfileCommand(Guid TenantId, Guid SenderProfileId, string Name, string SenderRef);

public static class UpdateSenderProfileHandler
{
    public static async Task<Result<SenderProfileResponse>> Handle(
        UpdateSenderProfileCommand command,
        ISenderProfileRepository senders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var sender = await senders.GetByIdAsync(command.TenantId, command.SenderProfileId, ct);
        if (sender is null)
            return Result.Failure<SenderProfileResponse>(SenderProfileErrors.NotFound);

        var updated = sender.Update(command.Name, command.SenderRef);
        if (updated.IsFailure)
            return Result.Failure<SenderProfileResponse>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(SenderProfileResponse.From(sender));
    }
}

// ─────────────────────────── Enable / Disable ───────────────────────────

public sealed record SetSenderProfileStatusCommand(Guid TenantId, Guid SenderProfileId, bool Active);

public static class SetSenderProfileStatusHandler
{
    public static async Task<Result<SenderProfileResponse>> Handle(
        SetSenderProfileStatusCommand command,
        ISenderProfileRepository senders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var sender = await senders.GetByIdAsync(command.TenantId, command.SenderProfileId, ct);
        if (sender is null)
            return Result.Failure<SenderProfileResponse>(SenderProfileErrors.NotFound);

        if (command.Active)
            sender.Activate();
        else
            sender.Disable();

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(SenderProfileResponse.From(sender));
    }
}
