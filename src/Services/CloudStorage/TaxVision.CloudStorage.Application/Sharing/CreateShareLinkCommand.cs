using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>
/// Fase C3 — crea un link de compartir sobre un File. ActorHasManagePermission lo
/// resuelve el controller a partir del claim "perm" == cloudstorage.share.manage
/// — Upload/EditMetadata solo son otorgables por un actor con ese permiso.
/// </summary>
public sealed record CreateShareLinkCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    bool ActorHasManagePermission,
    Guid FileId,
    ShareVisibility Visibility,
    SharePermission Permission,
    string? Password,
    DateTime? ExpiresAtUtc,
    int? MaxAccessCount,
    IReadOnlyList<Guid> RecipientUserIds,
    IReadOnlyList<Guid> RecipientCustomerIds,
    IReadOnlyList<string> RecipientEmails,
    RequestAuditContext Audit,
    // Idioma del email al destinatario externo ("Es"/"En"). Solo aplica a visibility ExternalRecipients.
    string? RecipientLanguage = null,
    // Cuando true, se acuña el share-link pero NO se publica el evento de invitación por email a los
    // destinatarios externos: el llamador (p. ej. Signature) entrega el enlace en su PROPIO correo y
    // el email genérico de CloudStorage "A document was shared with you" sería un duplicado. Default
    // false → el flujo normal de Documents sigue notificando.
    bool SuppressRecipientEmails = false
);

public static class CreateShareLinkHandler
{
    public static async Task<Result<CreatedShareLinkResponse>> Handle(
        CreateShareLinkCommand command,
        IFileObjectRepository files,
        IShareLinkRepository shares,
        IStorageLimitRepository limits,
        IShareLinkPasswordHasher passwordHasher,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var fileResult = await LoadShareableFile(command, files, ct);
        if (fileResult.IsFailure)
            return Result.Failure<CreatedShareLinkResponse>(fileResult.Error);

        return await ShareLinkCreationCore.CreateAndPersist(
            command.TenantId,
            command.ActorId,
            command.ActorHasManagePermission,
            command.FileId,
            ShareResourceType.File,
            command.Visibility,
            command.Permission,
            command.Password,
            command.ExpiresAtUtc,
            command.MaxAccessCount,
            isRecursive: false,
            appliesToFutureItems: false,
            command.RecipientUserIds,
            command.RecipientCustomerIds,
            command.RecipientEmails,
            fileResult.Value.OriginalName,
            command.RecipientLanguage ?? "En",
            command.Audit,
            shares,
            limits,
            passwordHasher,
            audit,
            clock,
            bus,
            unitOfWork,
            ct,
            command.SuppressRecipientEmails
        );
    }

    private static async Task<Result<FileObject>> LoadShareableFile(
        CreateShareLinkCommand command,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null || !command.Scope.CanAccess(file))
            return Result.Failure<FileObject>(FileErrors.NotFound);
        if (file.Status != FileStatus.Available)
            return Result.Failure<FileObject>(FileErrors.NotAvailable);
        return Result.Success(file);
    }
}
