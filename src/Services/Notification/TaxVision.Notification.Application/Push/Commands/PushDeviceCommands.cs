using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Push.Commands;

/// <summary>
/// Registra o reactiva un token de dispositivo push. Autoservicio — el
/// UserId viene del JWT verificado del caller (nunca del body), asi que un
/// usuario solo puede registrar tokens a su propio nombre.
/// </summary>
public sealed record RegisterPushDeviceTokenCommand(
    Guid TenantId,
    Guid UserId,
    PushPlatform Platform,
    string Token,
    string? DeviceId
);

public sealed record RegisterPushDeviceTokenResult(Guid Id, PushPlatform Platform, bool WasReactivated);

public static class RegisterPushDeviceTokenHandler
{
    public static async Task<Result<RegisterPushDeviceTokenResult>> Handle(
        RegisterPushDeviceTokenCommand command,
        IPushDeviceTokenRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await repository.FindByTokenAsync(command.TenantId, command.Token, ct);
        if (existing is not null)
        {
            existing.Reactivate(command.UserId, command.Platform, command.DeviceId);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(new RegisterPushDeviceTokenResult(existing.Id, existing.Platform, true));
        }

        var created = PushDeviceToken.Register(
            command.TenantId,
            command.UserId,
            command.Platform,
            command.Token,
            command.DeviceId
        );
        if (created.IsFailure)
            return Result.Failure<RegisterPushDeviceTokenResult>(created.Error);

        await repository.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new RegisterPushDeviceTokenResult(created.Value.Id, created.Value.Platform, false));
    }
}

/// <summary>Revoca un token — scoped a (TenantId, UserId): un usuario solo puede revocar SUS propios tokens.</summary>
public sealed record RevokePushDeviceTokenCommand(Guid TenantId, Guid UserId, Guid TokenId);

public static class RevokePushDeviceTokenHandler
{
    public static async Task<Result> Handle(
        RevokePushDeviceTokenCommand command,
        IPushDeviceTokenRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var token = await repository.GetAsync(command.TenantId, command.TokenId, ct);
        if (token is null || token.UserId != command.UserId)
            return Result.Failure(new Error("PushDeviceToken.NotFound", "Device token not found."));

        token.Revoke();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
