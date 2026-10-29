using BuildingBlocks.Common;
using BuildingBlocks.Imaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Avatars;

/// <summary>Foto de perfil propia del staff. Usuario y oficina salen del token, nunca del cuerpo.</summary>
public sealed record UploadMyAvatarCommand(Guid TenantId, Guid UserId, byte[] Content);

/// <summary><c>Status</c> es siempre "processing": la foto se confirma cuando CloudStorage termina el antivirus.</summary>
public sealed record UploadMyAvatarResponse(Guid FileId, string Status);

/// <summary>
/// Normaliza (512 px WebP sin metadatos), sube al bucket temporal, deja la foto pendiente y recién
/// después de guardar pide el catálogo a CloudStorage. Guardar antes de publicar: el resultado del
/// antivirus no puede llegar antes que la fila, y un reintento en línea no filtra el pedido.
/// </summary>
public static class UploadMyAvatarHandler
{
    public const string ProcessingStatus = "processing";

    public static async Task<Result<UploadMyAvatarResponse>> Handle(
        UploadMyAvatarCommand command,
        IUserRepository users,
        IAvatarImageNormalizer normalizer,
        IUserAvatarStorage storage,
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
            return Result.Failure<UploadMyAvatarResponse>(new Error("User.NotFound", "User does not exist."));

        // El cliente del portal tiene su foto en su ficha de Customer; se corta antes de gastar CPU.
        if (user.ActorType == UserActorType.CustomerPortal)
            return Result.Failure<UploadMyAvatarResponse>(UserAvatarErrors.NotAllowed);

        using var input = new MemoryStream(command.Content, writable: false);
        var normalized = await normalizer.NormalizeAsync(input, ct);
        if (normalized.IsFailure)
            return Result.Failure<UploadMyAvatarResponse>(normalized.Error);

        var avatar = normalized.Value;
        var stored = await storage.StoreAsync(
            user.TenantId,
            user.Id,
            Guid.NewGuid(),
            avatar.Content,
            avatar.ContentType,
            ct
        );
        if (stored.IsFailure)
            return Result.Failure<UploadMyAvatarResponse>(stored.Error);

        var begin = user.BeginAvatarChange(stored.Value.FileId, DateTime.UtcNow);
        if (begin.IsFailure)
            return Result.Failure<UploadMyAvatarResponse>(begin.Error);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.UserAvatarUploaded,
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
        await bus.PublishAsync(
            UserAvatarMessages.SaveFileRequested(
                user,
                stored.Value,
                avatar.ContentType,
                avatar.Content.LongLength,
                correlation.CorrelationId
            )
        );

        return Result.Success(new UploadMyAvatarResponse(stored.Value.FileId, ProcessingStatus));
    }
}

public static class UserAvatarErrors
{
    public static readonly Error NotAllowed = new(
        "User.AvatarNotAllowed",
        "Portal clients manage their photo from their client profile."
    );

    public static readonly Error StorageUnavailable = new(
        "User.AvatarStorageUnavailable",
        "The photo could not be stored right now. Please try again."
    );
}
