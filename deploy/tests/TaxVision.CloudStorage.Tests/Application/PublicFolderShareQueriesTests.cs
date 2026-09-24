using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>8.2 — navegación pública de una carpeta compartida y "Download all (ZIP)" por token.</summary>
public sealed class PublicFolderShareQueriesTests
{
    private static readonly RequestAuditContext Audit = new(null, null, "corr-1");

    private static Folder RootFolder(
        Guid tenantId,
        string name = "Clientes",
        Guid? parentId = null,
        string? parentPath = null
    ) =>
        Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                parentId,
                FolderName.Create(name).Value,
                parentPath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    private static FileObject FileInFolder(Guid tenantId, Guid folderId, DateTime assignedAt)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        file.MoveToFolder(folderId, assignedAt);
        return file;
    }

    private static (ShareLink Link, string Token) FolderLink(
        Guid tenantId,
        Guid folderId,
        DateTime now,
        SharePermission permission = SharePermission.Download,
        bool recursive = false,
        string? passwordHash = null
    ) =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                folderId,
                ShareResourceType.Folder,
                ShareVisibility.ExternalLink,
                permission,
                passwordHash,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: recursive,
                appliesToFutureItems: false
            )
            .Value;

    private static IOptions<CloudStorageOptions> Options(
        int maxFiles = 500,
        long maxAggregateBytes = 2L * 1024 * 1024 * 1024
    ) =>
        Microsoft.Extensions.Options.Options.Create(
            new CloudStorageOptions { MaxZipFiles = maxFiles, MaxZipAggregateBytes = maxAggregateBytes }
        );

    // ---------- Listado ----------

    [Fact]
    public async Task ListContents_recursive_returns_files_and_subfolders()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId, "Raiz");
        var child = RootFolder(tenantId, "Hijo", root.Id, root.RelativePath);
        var file = FileInFolder(tenantId, root.Id, now.AddMinutes(-10));
        var (link, token) = FolderLink(tenantId, root.Id, now, recursive: true);

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await ResolvePublicFolderContentsHandler.Handle(
            new ResolvePublicFolderContentsQuery(token, null, null, null),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeSystemClock(now),
            CancellationToken.None
        );

        Assert.Equal(PublicFolderOutcome.Available, result.Outcome);
        Assert.Equal("Raiz", result.FolderName);
        Assert.Single(result.Subfolders!);
        Assert.Single(result.Files!);
        Assert.Equal(file.Id, result.Files![0].FileId);
    }

    [Fact]
    public async Task ListContents_non_recursive_hides_subfolders()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId, "Raiz");
        var child = RootFolder(tenantId, "Hijo", root.Id, root.RelativePath);
        var (link, token) = FolderLink(tenantId, root.Id, now, recursive: false);

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await ResolvePublicFolderContentsHandler.Handle(
            new ResolvePublicFolderContentsQuery(token, null, null, null),
            shares,
            new FakeFileObjectRepository(),
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeSystemClock(now),
            CancellationToken.None
        );

        Assert.Equal(PublicFolderOutcome.Available, result.Outcome);
        Assert.Empty(result.Subfolders!);
    }

    [Fact]
    public async Task ListContents_requires_password_when_the_link_has_one()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId);
        var (link, token) = FolderLink(tenantId, root.Id, now, passwordHash: "hashed");

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var folders = new FakeFolderRepository();
        folders.Seed(root);

        var result = await ResolvePublicFolderContentsHandler.Handle(
            new ResolvePublicFolderContentsQuery(token, null, null, null),
            shares,
            new FakeFileObjectRepository(),
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeSystemClock(now),
            CancellationToken.None
        );

        Assert.Equal(PublicFolderOutcome.PasswordRequired, result.Outcome);
    }

    [Fact]
    public async Task ListContents_denies_an_unknown_token()
    {
        var result = await ResolvePublicFolderContentsHandler.Handle(
            new ResolvePublicFolderContentsQuery("nope", null, null, null),
            new FakeShareLinkRepository(),
            new FakeFileObjectRepository(),
            new FakeFolderRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Equal(PublicFolderOutcome.Denied, result.Outcome);
    }

    // ---------- ZIP ----------

    [Fact]
    public async Task Zip_ready_builds_a_plan_and_counts_one_access()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId, "Raiz");
        var child = RootFolder(tenantId, "Hijo", root.Id, root.RelativePath);
        var file = FileInFolder(tenantId, child.Id, now.AddMinutes(-10));
        var (link, token) = FolderLink(tenantId, root.Id, now, SharePermission.Download, recursive: true);

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await PreparePublicFolderZipHandler.Handle(
            new PreparePublicFolderZipQuery(token, null, null, null, Audit),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(PublicZipOutcome.Ready, result.Outcome);
        Assert.Single(result.Plan!.Entries);
        Assert.StartsWith("Raiz/", result.Plan!.Entries[0].EntryName);
        Assert.Equal("Raiz.zip", result.ArchiveName);
        Assert.Equal(1, link.AccessCount);
    }

    [Fact]
    public async Task Zip_denied_when_permission_is_not_download()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId);
        var file = FileInFolder(tenantId, root.Id, now.AddMinutes(-10));
        var (link, token) = FolderLink(tenantId, root.Id, now, SharePermission.View);

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);

        var result = await PreparePublicFolderZipHandler.Handle(
            new PreparePublicFolderZipQuery(token, null, null, null, Audit),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(PublicZipOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task Zip_too_large_when_file_count_exceeds_cap()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId);
        var file = FileInFolder(tenantId, root.Id, now.AddMinutes(-10));
        var (link, token) = FolderLink(tenantId, root.Id, now, SharePermission.Download);

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);

        var result = await PreparePublicFolderZipHandler.Handle(
            new PreparePublicFolderZipQuery(token, null, null, null, Audit),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(maxFiles: 0),
            new FakeSystemClock(now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(PublicZipOutcome.TooLarge, result.Outcome);
    }

    [Fact]
    public async Task Zip_too_large_when_aggregate_bytes_exceed_cap()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId);
        var file = FileInFolder(tenantId, root.Id, now.AddMinutes(-10));
        var (link, token) = FolderLink(tenantId, root.Id, now, SharePermission.Download);

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);

        var result = await PreparePublicFolderZipHandler.Handle(
            new PreparePublicFolderZipQuery(token, null, null, null, Audit),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(maxAggregateBytes: 1), // el archivo semilla pesa 10 bytes
            new FakeSystemClock(now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(PublicZipOutcome.TooLarge, result.Outcome);
    }

    [Fact]
    public async Task Zip_requires_password_when_the_link_has_one()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId);
        var file = FileInFolder(tenantId, root.Id, now.AddMinutes(-10));
        var (link, token) = FolderLink(tenantId, root.Id, now, SharePermission.Download, passwordHash: "hashed");

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);

        var result = await PreparePublicFolderZipHandler.Handle(
            new PreparePublicFolderZipQuery(token, null, null, null, Audit),
            shares,
            files,
            folders,
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(PublicZipOutcome.PasswordRequired, result.Outcome);
    }
}
