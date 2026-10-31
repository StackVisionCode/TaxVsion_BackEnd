using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Avatars;

/// <summary>
/// Resultado del antivirus de CloudStorage para las fotos de perfil del staff (User/Avatars). Solo
/// actúa sobre la foto pendiente del usuario: un evento viejo o repetido no cambia nada. Una foto que
/// quedó disponible pero ya no la referencia nadie (se subió otra encima, se quitó o venció) se manda
/// a borrar para no dejar huérfanos.
/// </summary>
public static class UserAvatarScanResultConsumer
{
    public const string ReplacedReason = "avatar_replaced";
    public const string StaleReason = "avatar_stale";

    public static async Task Handle(
        FileAvailableIntegrationEvent msg,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    )
    {
        if (!UserAvatarMessages.IsUserAvatar(msg.OwnerType, msg.FolderType, msg.OwnerId))
            return;

        using var _ = correlation.Push(CorrelationOf(msg.CorrelationId, msg.EventId));

        var user = await FindOwnerAsync(users, msg.TenantId, msg.OwnerId!.Value, msg.FileId, logger, ct);
        if (user is null)
            return;

        // Redelivery de una foto ya confirmada: nada que hacer.
        if (user.AvatarFileId == msg.FileId)
            return;

        if (user.PendingAvatarFileId != msg.FileId)
        {
            await RequestDeletionAsync(user, msg.FileId, StaleReason, bus, correlation);
            return;
        }

        // Llegó tarde: el perfil ya la reportó como fallida, así que se trata igual.
        if (!user.HasLivePendingAvatar(DateTime.UtcNow))
        {
            user.DiscardPendingAvatar(msg.FileId);
            await unitOfWork.SaveChangesAsync(ct);
            await RequestDeletionAsync(user, msg.FileId, StaleReason, bus, correlation);
            return;
        }

        var previous = user.ConfirmAvatar(msg.FileId);
        await unitOfWork.SaveChangesAsync(ct);

        if (previous is { } previousFileId)
            await RequestDeletionAsync(user, previousFileId, ReplacedReason, bus, correlation);
        await bus.PublishAsync(UserAvatarMessages.ProfileUpdated(user, correlation.CorrelationId));

        logger.LogInformation("User {UserId} avatar confirmed: file {FileId}.", user.Id, msg.FileId);
    }

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent msg,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            new ScanFailure(msg.TenantId, msg.FileId, msg.OwnerType, msg.OwnerId, msg.FolderType, "infected"),
            CorrelationOf(msg.CorrelationId, msg.EventId),
            users,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    // T-20: bloqueada por contenido. Se descarta igual que un rechazo, pero queda constancia para el mensaje.
    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent msg,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    ) =>
        ModerateAsync(
            new ScanFailure(msg.TenantId, msg.FileId, msg.OwnerType, msg.OwnerId, msg.FolderType, "blocked_by_policy"),
            CorrelationOf(msg.CorrelationId, msg.EventId),
            (user, fileId) => user.RejectPendingAvatarForPolicy(fileId),
            users,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    // T-20: en revisión humana. La pendiente no vence; si el revisor aprueba, llega FileAvailable.
    public static Task Handle(
        FilePendingReviewIntegrationEvent msg,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    ) =>
        ModerateAsync(
            new ScanFailure(msg.TenantId, msg.FileId, msg.OwnerType, msg.OwnerId, msg.FolderType, "pending_review"),
            CorrelationOf(msg.CorrelationId, msg.EventId),
            (user, fileId) => user.HoldPendingAvatarForReview(fileId),
            users,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        FileScanFailedIntegrationEvent msg,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            new ScanFailure(msg.TenantId, msg.FileId, msg.OwnerType, msg.OwnerId, msg.FolderType, msg.Reason),
            CorrelationOf(msg.CorrelationId, msg.EventId),
            users,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private static Task DiscardAsync(
        ScanFailure failure,
        string correlationId,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    ) =>
        ModerateAsync(
            failure,
            correlationId,
            (user, fileId) => user.DiscardPendingAvatar(fileId),
            users,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private static async Task ModerateAsync(
        ScanFailure failure,
        string correlationId,
        Func<User, Guid, bool> apply,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<User> logger,
        CancellationToken ct
    )
    {
        if (!UserAvatarMessages.IsUserAvatar(failure.OwnerType, failure.FolderType, failure.OwnerId))
            return;

        using var _ = correlation.Push(correlationId);

        var user = await FindOwnerAsync(users, failure.TenantId, failure.OwnerId!.Value, failure.FileId, logger, ct);
        if (user is null || !apply(user, failure.FileId))
            return;

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "User {UserId} avatar file {FileId} moderated by CloudStorage ({Reason}).",
            user.Id,
            failure.FileId,
            failure.Reason
        );
    }

    private static async Task<User?> FindOwnerAsync(
        IUserRepository users,
        Guid tenantId,
        Guid userId,
        Guid fileId,
        ILogger<User> logger,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null)
            return null;

        if (user.TenantId == tenantId)
            return user;

        logger.LogWarning(
            "Avatar file {FileId} event carries tenant {EventTenantId} but user {UserId} belongs to another tenant; ignored.",
            fileId,
            tenantId,
            userId
        );
        return null;
    }

    private static async Task RequestDeletionAsync(
        User user,
        Guid fileId,
        string reason,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var deletion = UserAvatarMessages.FilesDeletionRequested(user, [fileId], reason, correlation.CorrelationId);
        if (deletion is not null)
            await bus.PublishAsync(deletion);
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;

    private sealed record ScanFailure(
        Guid TenantId,
        Guid FileId,
        string? OwnerType,
        Guid? OwnerId,
        string? FolderType,
        string Reason
    );
}
