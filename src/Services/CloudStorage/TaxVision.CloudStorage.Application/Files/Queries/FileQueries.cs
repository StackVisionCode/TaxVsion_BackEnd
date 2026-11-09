using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Queries;

public sealed record GetFileQuery(Guid TenantId, StorageActorScope Scope, Guid FileId);

public static class GetFileHandler
{
    public static async Task<Result<FileResponse>> Handle(
        GetFileQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(query.TenantId, query.FileId, ct);
        return file is null || !query.Scope.CanAccess(file)
            ? Result.Failure<FileResponse>(FileErrors.NotFound)
            : Result.Success(FileResponseMapper.Map(file, query.Scope));
    }
}

public sealed record ListFilesQuery(
    Guid TenantId,
    StorageActorScope Scope,
    OwnerType? OwnerType,
    Guid? OwnerId,
    int Skip,
    int Take,
    // Null = todo menos Avatars; las fotos de perfil solo se listan si se piden explícito.
    FolderType? FolderType = null
);

public static class ListFilesHandler
{
    public static async Task<IReadOnlyList<FileResponse>> Handle(
        ListFilesQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var raw = await files.ListAsync(
            query.TenantId,
            query.Scope.IsCustomerPortal ? query.Scope.CustomerId ?? Guid.Empty : null,
            query.Scope.BypassAssignmentFilter ? null : query.Scope.AssignedCustomerIds,
            query.OwnerType,
            query.OwnerId,
            query.FolderType,
            Math.Max(0, query.Skip),
            Math.Clamp(query.Take, 1, 100),
            ct
        );
        // Portal: oculta items con deny Visible/Preview (Fase 4). Staff/flag off = sin filtro.
        var filtered = query.Scope.HasAnyPortalDenyRules
            ? raw.Where(f =>
                !query.Scope.IsHiddenForPortal(
                    TaxVision.CloudStorage.Domain.CustomerAccess.AccessResourceType.File,
                    f.Id
                )
            )
            : raw;
        return filtered.Select(f => FileResponseMapper.Map(f, query.Scope)).ToArray();
    }
}

/// <summary>
/// <paramref name="ForPreview"/>: la URL es para VER el archivo en el navegador (visor), no para bajarlo. Se
/// juzga con la capacidad Preview y se sirve <c>inline</c>; así un cliente "Preview-only" puede verlo
/// aunque la descarga esté bloqueada. La descarga (false) sigue exigiendo Download y sale como adjunto.
/// </summary>
public sealed record IssueDownloadUrlQuery(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FileId,
    RequestAuditContext Audit,
    bool ForPreview = false
);

public static class IssueDownloadUrlHandler
{
    public static async Task<Result<DownloadUrlResponse>> Handle(
        IssueDownloadUrlQuery query,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        IStoragePlatformSettingsProvider platform,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(query.TenantId, query.FileId, ct);
        if (file is null)
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotFound);
        if (!query.Scope.CanAccess(file))
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotFound);
        // Portal con deny de la capacidad pedida (Download o, para el visor, Preview): 403 con reason.
        var capability = query.ForPreview ? AccessCapability.Preview : AccessCapability.Download;
        if (query.Scope.IsDeniedForPortal(AccessResourceType.File, file.Id, capability))
            return Result.Failure<DownloadUrlResponse>(
                CustomerAccessDenied.With(query.Scope.DenyReasonFor(AccessResourceType.File, file.Id, capability))
            );
        if (file.Status != FileStatus.Available)
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotAvailable);

        var lifetime = TimeSpan.FromMinutes((await platform.GetAsync(ct)).SignedUrlMinutes);
        // Descarga: adjunto con el nombre real. Visor: inline (lo pinta el navegador, no lo guarda).
        var disposition = ContentDispositionBuilder.Build(
            query.ForPreview ? "inline" : "attachment",
            file.OriginalName
        );
        var action = query.ForPreview ? "preview.url-issued" : "download.url-issued";
        var url = await storage.PresignGetAsync(options.Value.MainBucket, file.ObjectKey, lifetime, disposition, ct);
        audit.Add(
            StorageAccessLog.Create(
                query.TenantId,
                file.Id,
                query.ActorId,
                action,
                "success",
                query.Audit.IpAddress,
                query.Audit.UserAgent,
                query.Audit.CorrelationId,
                null,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = query.TenantId,
                FileId = file.Id,
                ActorId = query.ActorId,
                Action = action,
                Outcome = "success",
                IpAddress = query.Audit.IpAddress,
                CorrelationId = query.Audit.CorrelationId,
            }
        );
        return Result.Success(new DownloadUrlResponse(file.Id, url, clock.UtcNow.Add(lifetime)));
    }
}
