using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Fase C4 — compartir carpetas: creacion (IsRecursive/AppliesToFutureItems),
/// herencia evaluada en tiempo de acceso (FolderShareCoverage via resolucion
/// publica/privada), y la alerta al mover un archivo a una carpeta con un link
/// Public activo (MoveFileToFolderHandler).
/// </summary>
public sealed class FolderShareHandlerTests
{
    private static readonly StorageActorScope TenantScope = new(false, null);

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

    private static FileObject RegisteredFile(Guid tenantId)
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
        return file;
    }

    private static IOptions<CloudStorageOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CloudStorageOptions());

    // ---------- CreateFolderShareLinkHandler ----------

    [Fact]
    public async Task CreateForFolder_succeeds_with_recursive_and_future_items_flags()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await CreateFolderShareLinkHandler.Handle(
            new CreateFolderShareLinkCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                false,
                folder.Id,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                true,
                true,
                [],
                [],
                [],
                new RequestAuditContext(null, null, "corr-1")
            ),
            folders,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateForFolder_of_a_folder_belonging_to_another_tenant_is_not_found()
    {
        var folder = RootFolder(Guid.NewGuid());
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await CreateFolderShareLinkHandler.Handle(
            new CreateFolderShareLinkCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TenantScope,
                false,
                folder.Id,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                false,
                false,
                [],
                [],
                [],
                new RequestAuditContext(null, null, "corr-1")
            ),
            folders,
            new FakeShareLinkRepository(),
            new FakeStorageLimitRepository(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotFound, result.Error);
    }

    // ---------- FolderShareCoverage (via ResolvePublicShareHandler) ----------

    [Fact]
    public async Task ResolvePublic_folder_link_serves_a_direct_child_file()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(folder.Id, now.AddMinutes(-10)); // ya estaba en la carpeta antes de crear el link

        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                folder.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: false,
                appliesToFutureItems: false
            )
            .Value;

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, file.Id, null, null),
            shares,
            files,
            folders,
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_non_recursive_folder_link_does_not_reach_a_grandchild_file()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId, "Raiz");
        var child = RootFolder(tenantId, "Hijo", root.Id, root.RelativePath);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(child.Id, now.AddMinutes(-10));

        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                root.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: false,
                appliesToFutureItems: false
            )
            .Value;

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, file.Id, null, null),
            shares,
            files,
            folders,
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_recursive_folder_link_reaches_a_grandchild_file()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId, "Raiz");
        var child = RootFolder(tenantId, "Hijo", root.Id, root.RelativePath);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(child.Id, now.AddMinutes(-10));

        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                root.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: true,
                appliesToFutureItems: false
            )
            .Value;

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, file.Id, null, null),
            shares,
            files,
            folders,
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_denies_a_file_added_after_the_link_when_AppliesToFutureItems_is_false()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);

        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                folder.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: false,
                appliesToFutureItems: false
            )
            .Value;
        file.MoveToFolder(folder.Id, now.AddMinutes(10)); // entra a la carpeta DESPUES de crear el link

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, file.Id, null, null),
            shares,
            files,
            folders,
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now.AddMinutes(20)),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task ResolvePublic_covers_a_file_added_after_the_link_when_AppliesToFutureItems_is_true()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);

        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                folder.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: false,
                appliesToFutureItems: true
            )
            .Value;
        file.MoveToFolder(folder.Id, now.AddMinutes(10)); // entra a la carpeta DESPUES de crear el link

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await ResolvePublicShareHandler.Handle(
            new ResolvePublicShareQuery(token, null, null, file.Id, null, null),
            shares,
            files,
            folders,
            new FakeObjectStorage(),
            new FakeShareLinkPasswordHasher(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now.AddMinutes(20)),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
    }

    [Fact]
    public async Task ResolvePrivate_TenantOnly_folder_link_serves_a_file_via_recursive_ancestor()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var root = RootFolder(tenantId, "Raiz");
        var child = RootFolder(tenantId, "Hijo", root.Id, root.RelativePath);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(child.Id, now.AddMinutes(-10));

        var (link, token) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                root.Id,
                ShareResourceType.Folder,
                ShareVisibility.TenantOnly,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                now,
                isRecursive: true,
                appliesToFutureItems: false
            )
            .Value;

        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await ResolvePrivateShareHandler.Handle(
            new ResolvePrivateShareQuery(token, tenantId, Guid.NewGuid(), TenantScope, file.Id, null, null),
            shares,
            files,
            folders,
            new FakeObjectStorage(),
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(now),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(ShareAccessOutcome.Redirect, result.Outcome);
    }

    // ---------- MoveFileToFolderHandler — alerta al agregar un file a una carpeta con link Public activo ----------

    [Fact]
    public async Task MoveFileToFolder_alerts_and_marks_not_auto_covered_when_the_link_does_not_apply_to_future_items()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        var (link, _) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                folder.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                isRecursive: false,
                appliesToFutureItems: false
            )
            .Value;

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();

        var result = await MoveFileToFolderHandler.Handle(
            new MoveFileToFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                file.Id,
                folder.Id,
                new RequestAuditContext(null, null, "corr-1")
            ),
            files,
            folders,
            shares,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(folder.Id, file.FolderId);
        var alert = Assert.Single(bus.Published.OfType<ShareLinkFolderItemAddedIntegrationEvent>());
        Assert.Equal(link.Id, alert.ShareLinkId);
        Assert.Equal(file.Id, alert.FileId);
        Assert.False(alert.AutoCovered);
        Assert.Single(audit.Logs, log => log.Action == "share.folder-item-added");
    }

    [Fact]
    public async Task MoveFileToFolder_marks_auto_covered_when_the_link_applies_to_future_items()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        var (link, _) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                folder.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                isRecursive: false,
                appliesToFutureItems: true
            )
            .Value;

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var bus = new FakeMessageBus();

        var result = await MoveFileToFolderHandler.Handle(
            new MoveFileToFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                file.Id,
                folder.Id,
                new RequestAuditContext(null, null, "corr-1")
            ),
            files,
            folders,
            shares,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var alert = Assert.Single(bus.Published.OfType<ShareLinkFolderItemAddedIntegrationEvent>());
        Assert.True(alert.AutoCovered);
    }

    [Fact]
    public async Task MoveFileToFolder_does_not_alert_when_a_non_recursive_link_is_on_a_grandparent()
    {
        var tenantId = Guid.NewGuid();
        var grandparent = RootFolder(tenantId, "Abuelo");
        var parent = RootFolder(tenantId, "Padre", grandparent.Id, grandparent.RelativePath);
        var file = RegisteredFile(tenantId);
        var (link, _) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                grandparent.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                isRecursive: false,
                appliesToFutureItems: false
            )
            .Value;

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(grandparent);
        folders.Seed(parent);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var bus = new FakeMessageBus();

        var result = await MoveFileToFolderHandler.Handle(
            new MoveFileToFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                file.Id,
                parent.Id,
                new RequestAuditContext(null, null, "corr-1")
            ),
            files,
            folders,
            shares,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task MoveFileToFolder_alerts_when_a_recursive_link_is_on_a_grandparent()
    {
        var tenantId = Guid.NewGuid();
        var grandparent = RootFolder(tenantId, "Abuelo");
        var parent = RootFolder(tenantId, "Padre", grandparent.Id, grandparent.RelativePath);
        var file = RegisteredFile(tenantId);
        var (link, _) = ShareLink
            .Create(
                Guid.NewGuid(),
                tenantId,
                grandparent.Id,
                ShareResourceType.Folder,
                ShareVisibility.Public,
                SharePermission.View,
                null,
                null,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                isRecursive: true,
                appliesToFutureItems: false
            )
            .Value;

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(grandparent);
        folders.Seed(parent);
        var shares = new FakeShareLinkRepository();
        shares.Seed(link);
        var bus = new FakeMessageBus();

        var result = await MoveFileToFolderHandler.Handle(
            new MoveFileToFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                file.Id,
                parent.Id,
                new RequestAuditContext(null, null, "corr-1")
            ),
            files,
            folders,
            shares,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var alert = Assert.Single(bus.Published.OfType<ShareLinkFolderItemAddedIntegrationEvent>());
        Assert.Equal(grandparent.Id, alert.FolderId);
    }

    // ---------- ListShareLinksForFolderHandler ----------

    [Fact]
    public async Task ListForFolder_is_not_found_for_a_folder_owned_by_another_tenant()
    {
        var folder = RootFolder(Guid.NewGuid());
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await ListShareLinksForFolderHandler.Handle(
            new ListShareLinksForFolderQuery(Guid.NewGuid(), TenantScope, folder.Id),
            folders,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotFound, result.Error);
    }
}
