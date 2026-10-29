using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Avatars;

public sealed record RemoveMyAvatarCommand(Guid TenantId, Guid UserId);

/// <summary>
/// Quita la foto confirmada y la pendiente, pide su borrado a CloudStorage y avisa el perfil nuevo.
/// Idempotente: sin foto no publica nada.
/// </summary>
public static class RemoveMyAvatarHandler
{
    public const string Reason = "avatar_removed";

    public static async Task<Result> Handle(
        RemoveMyAvatarCommand command,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive || user.TenantId != command.TenantId)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        if (user.ActorType == UserActorType.CustomerPortal)
            return Result.Failure(UserAvatarErrors.NotAllowed);

        var hadConfirmed = user.AvatarFileId.HasValue;
        var removed = user.RemoveAvatar();
        if (removed.Count == 0)
            return Result.Success();

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.UserAvatarRemoved,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: user.Id
            ),
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        var deletion = UserAvatarMessages.FilesDeletionRequested(user, removed, Reason, correlation.CorrelationId);
        if (deletion is not null)
            await bus.PublishAsync(deletion);
        // Solo una foto pendiente no cambiaba lo que veían los demás.
        if (hadConfirmed)
            await bus.PublishAsync(UserAvatarMessages.ProfileUpdated(user, correlation.CorrelationId));

        return Result.Success();
    }
}
