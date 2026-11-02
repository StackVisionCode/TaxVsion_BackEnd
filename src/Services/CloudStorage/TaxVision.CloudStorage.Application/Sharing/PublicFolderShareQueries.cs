using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Sharing;

// ===================== Navegación pública de una carpeta compartida (8.2) =====================

public enum PublicFolderOutcome
{
    Available,
    PasswordRequired,
    Denied,

    /// <summary>Demasiadas contraseñas fallidas: el enlace no acepta intentos por un rato.</summary>
    PasswordLocked,
}

public sealed record PublicFolderCrumb(Guid FolderId, string Name);

public sealed record PublicFolderSubfolder(Guid FolderId, string Name);

public sealed record PublicFolderFile(Guid FileId, string Name, long SizeBytes, string ContentType);

public sealed record PublicFolderContentsResult(
    PublicFolderOutcome Outcome,
    Guid? FolderId = null,
    string? FolderName = null,
    bool IsRecursive = false,
    string? Permission = null,
    DateTime? ExpiresAtUtc = null,
    IReadOnlyList<PublicFolderCrumb>? Breadcrumb = null,
    IReadOnlyList<PublicFolderSubfolder>? Subfolders = null,
    IReadOnlyList<PublicFolderFile>? Files = null
)
{
    public static PublicFolderContentsResult Denied() => new(PublicFolderOutcome.Denied);

    public static PublicFolderContentsResult NeedsPassword() => new(PublicFolderOutcome.PasswordRequired);
}

/// <summary>Navega el contenido de una carpeta compartida por token (sin servir binarios). folderId null = raíz del share.</summary>
public sealed record ResolvePublicFolderContentsQuery(
    string Token,
    string? Password,
    string? RecipientEmail,
    Guid? FolderId
);

public static class ResolvePublicFolderContentsHandler
{
    public static async Task<PublicFolderContentsResult> Handle(
        ResolvePublicFolderContentsQuery query,
        IShareLinkRepository shares,
        IFileObjectRepository files,
        IFolderRepository folders,
        IShareLinkPasswordHasher passwordHasher,
        IStorageLimitRepository limits,
        IStoragePlatformSettingsProvider platform,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetByTokenHashAsync(ShareToken.HashOf(query.Token), ct);
        var gate = await PublicFolderShareGate.CheckAsync(
            link,
            query.RecipientEmail,
            query.Password,
            passwordHasher,
            limits,
            platform,
            clock.UtcNow,
            unitOfWork,
            ct
        );
        if (gate != PublicFolderOutcome.Available)
            return new PublicFolderContentsResult(gate);

        var targetFolderId = query.FolderId ?? link!.ResourceId;
        if (!await FolderShareCoverage.FolderWithinShareAsync(link!, targetFolderId, folders, ct))
            return PublicFolderContentsResult.Denied();

        var folder = await folders.GetAsync(link!.TenantId, targetFolderId, ct);
        if (folder is null)
            return PublicFolderContentsResult.Denied();

        var subfolders = link.IsRecursive ? (
                // Share público: no se aplica filtro ABAC (el token ES la autorización).
                await folders.ListSubfoldersAsync(
                    link.TenantId,
                    targetFolderId,
                    null,
                    null,
                    null,
                    null,
                    FolderContentsFilter.None,
                    null,
                    null,
                    ct
                )
            ).Select(f => new PublicFolderSubfolder(f.Id, f.Name)).ToList() : [];

        var folderFiles = await files.ListInFolderAsync(
            link.TenantId,
            targetFolderId,
            null,
            null,
            null,
            null,
            FolderContentsFilter.None,
            null,
            null,
            ct
        );
        var visibleFiles = folderFiles
            .Where(f => f.Status == FileStatus.Available && FolderShareCoverage.IncludesFileByTime(link, f))
            .Select(f => new PublicFolderFile(
                f.Id,
                f.OriginalName,
                f.SizeBytes,
                f.DetectedContentType ?? f.DeclaredContentType
            ))
            .ToList();

        var breadcrumb = await BuildBreadcrumbAsync(link, targetFolderId, folders, ct);

        return new PublicFolderContentsResult(
            PublicFolderOutcome.Available,
            targetFolderId,
            folder.Name,
            link.IsRecursive,
            link.Permission.ToString(),
            link.ExpiresAtUtc,
            breadcrumb,
            subfolders,
            visibleFiles
        );
    }

    private static async Task<IReadOnlyList<PublicFolderCrumb>> BuildBreadcrumbAsync(
        ShareLink link,
        Guid targetFolderId,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var trail = new List<PublicFolderCrumb>();
        var currentFolderId = (Guid?)targetFolderId;
        for (var depth = 0; depth < 64 && currentFolderId is { } id; depth++)
        {
            var folder = await folders.GetAsync(link.TenantId, id, ct);
            if (folder is null)
                break;
            trail.Add(new PublicFolderCrumb(folder.Id, folder.Name));
            if (id == link.ResourceId) // no exponer ancestros por encima de la raíz del share
                break;
            currentFolderId = folder.ParentFolderId;
        }
        trail.Reverse();
        return trail;
    }
}

// ===================== ZIP público de una carpeta compartida (8.2) =====================

public enum PublicZipOutcome
{
    Ready,
    PasswordRequired,
    Denied,
    TooLarge,
    PasswordLocked,
}

public sealed record PublicZipResult(PublicZipOutcome Outcome, ZipDownloadPlan? Plan = null, string? ArchiveName = null)
{
    public static PublicZipResult Denied() => new(PublicZipOutcome.Denied);

    public static PublicZipResult NeedsPassword() => new(PublicZipOutcome.PasswordRequired);

    public static PublicZipResult TooLarge() => new(PublicZipOutcome.TooLarge);

    public static PublicZipResult PasswordLocked() => new(PublicZipOutcome.PasswordLocked);
}

/// <summary>Arma el plan del "Download all (ZIP)" de una carpeta compartida por token. No escribe bytes (eso lo streamea el controller).</summary>
public sealed record PreparePublicFolderZipQuery(
    string Token,
    string? Password,
    string? RecipientEmail,
    Guid? FolderId,
    RequestAuditContext Audit
);

public static class PreparePublicFolderZipHandler
{
    public static async Task<PublicZipResult> Handle(
        PreparePublicFolderZipQuery query,
        IShareLinkRepository shares,
        IFileObjectRepository files,
        IFolderRepository folders,
        IShareLinkPasswordHasher passwordHasher,
        IStorageLimitRepository limits,
        IStoragePlatformSettingsProvider platform,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = clock.UtcNow;
        var link = await shares.GetByTokenHashAsync(ShareToken.HashOf(query.Token), ct);
        var gate = await PublicFolderShareGate.CheckAsync(
            link,
            query.RecipientEmail,
            query.Password,
            passwordHasher,
            limits,
            platform,
            now,
            unitOfWork,
            ct
        );
        if (gate == PublicFolderOutcome.PasswordRequired)
            return PublicZipResult.NeedsPassword();
        if (gate == PublicFolderOutcome.PasswordLocked)
            return PublicZipResult.PasswordLocked();
        if (gate != PublicFolderOutcome.Available)
            return PublicZipResult.Denied();

        // "Download all" es una descarga: exige permiso Download.
        if (link!.Permission != SharePermission.Download)
            return PublicZipResult.Denied();

        var targetFolderId = query.FolderId ?? link.ResourceId;
        if (!await FolderShareCoverage.FolderWithinShareAsync(link, targetFolderId, folders, ct))
            return PublicZipResult.Denied();

        var root = await folders.GetAsync(link.TenantId, targetFolderId, ct);
        if (root is null)
            return PublicZipResult.Denied();

        var config = await platform.GetAsync(ct);

        // Prefijos de carpeta: la raíz aporta su nombre; si es recursivo, los descendientes cuelgan por RelativePath.
        var folderEntryPrefixes = new Dictionary<Guid, string> { [root.Id] = root.Name };
        var allFolderIds = new List<Guid> { root.Id };
        if (link.IsRecursive)
        {
            var descendants = await folders.ListByPathPrefixAsync(link.TenantId, root.RelativePath, ct);
            foreach (var descendant in descendants)
            {
                var suffix = descendant.RelativePath[(root.RelativePath.Length + 1)..];
                folderEntryPrefixes[descendant.Id] = $"{root.Name}/{suffix}";
                allFolderIds.Add(descendant.Id);
            }
        }
        if (allFolderIds.Count > config.MaxZipFolders)
            return PublicZipResult.TooLarge();

        // Share público: no se aplica filtro ABAC.
        var folderFiles = await files.ListInFoldersAsync(link.TenantId, allFolderIds, null, null, ct);
        var combined = folderFiles
            .Where(f => f.Status == FileStatus.Available && FolderShareCoverage.IncludesFileByTime(link, f))
            .Select(f => (File: f, FolderPrefix: (string?)folderEntryPrefixes[f.FolderId!.Value]))
            .ToList();

        if (combined.Count == 0)
            return PublicZipResult.Denied();
        if (combined.Count > config.MaxZipFiles)
            return PublicZipResult.TooLarge();
        if (combined.Sum(item => item.File.SizeBytes) > config.MaxZipAggregateBytes)
            return PublicZipResult.TooLarge();

        var entries = PrepareZipDownloadHandler.BuildEntries(combined);

        link.RegisterAccess(now); // el ZIP cuenta como UN acceso
        audit.Add(
            StorageAccessLog.Create(
                link.TenantId,
                null,
                link.CreatedByUserId,
                "folder.zip.download",
                "success",
                query.Audit.IpAddress,
                query.Audit.UserAgent,
                link.Id.ToString(),
                $"files={combined.Count};bytes={combined.Sum(i => i.File.SizeBytes)}",
                now
            )
        );
        await unitOfWork.SaveChangesAsync(ct);

        var archiveName = ContentDispositionBuilder.SafeAsciiFileName(root.Name) + ".zip";
        return new PublicZipResult(PublicZipOutcome.Ready, new ZipDownloadPlan(entries), archiveName);
    }
}

/// <summary>Puerta común de los flujos públicos de folder: token usable + servible por endpoint público + password.</summary>
internal static class PublicFolderShareGate
{
    public static async Task<PublicFolderOutcome> CheckAsync(
        ShareLink? link,
        string? recipientEmail,
        string? password,
        IShareLinkPasswordHasher passwordHasher,
        IStorageLimitRepository limits,
        IStoragePlatformSettingsProvider platform,
        DateTime nowUtc,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (link is null || !link.IsUsable(nowUtc) || link.ResourceType != ShareResourceType.Folder)
            return PublicFolderOutcome.Denied;

        var servedByPublic = link.Visibility switch
        {
            ShareVisibility.Public => true,
            ShareVisibility.ExternalLink => true,
            ShareVisibility.ExternalRecipients => !string.IsNullOrWhiteSpace(recipientEmail)
                && link.HasEmailRecipient(recipientEmail),
            _ => false,
        };
        if (!servedByPublic)
            return PublicFolderOutcome.Denied;

        var outcome = await SharePasswordGuard.CheckAsync(link, password, passwordHasher, limits, platform, nowUtc, ct);
        // El guardia movió los contadores del enlace: se guardan ya, aunque el llamador niegue después.
        if (outcome is SharePasswordOutcome.Accepted or SharePasswordOutcome.Wrong or SharePasswordOutcome.Locked)
            await unitOfWork.SaveChangesAsync(ct);

        return outcome switch
        {
            SharePasswordOutcome.Missing => PublicFolderOutcome.PasswordRequired,
            SharePasswordOutcome.Wrong => PublicFolderOutcome.Denied,
            SharePasswordOutcome.Locked => PublicFolderOutcome.PasswordLocked,
            _ => PublicFolderOutcome.Available,
        };
    }
}
