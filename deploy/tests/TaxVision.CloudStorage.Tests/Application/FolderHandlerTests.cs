using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase C2 — CreateFolderHandler, RenameFolderHandler, MoveFolderHandler, MoveFileToFolderHandler, GetFolderContentsHandler.</summary>
public sealed class FolderHandlerTests
{
    private static readonly StorageActorScope TenantScope = new(false, null);

    private static Folder RootFolder(Guid tenantId, string name = "Clientes") =>
        Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                null,
                FolderName.Create(name).Value,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Create_at_root_succeeds_and_persists()
    {
        var tenantId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateFolderHandler.Handle(
            new CreateFolderCommand(tenantId, Guid.NewGuid(), TenantScope, null, "Clientes", OwnerType.Tenant, null),
            folders,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("/Clientes", result.Value.RelativePath);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_under_the_same_parent()
    {
        var tenantId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        folders.Seed(RootFolder(tenantId, "Clientes"));

        var result = await CreateFolderHandler.Handle(
            new CreateFolderCommand(tenantId, Guid.NewGuid(), TenantScope, null, "Clientes", OwnerType.Tenant, null),
            folders,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NameAlreadyExists, result.Error);
    }

    [Fact]
    public async Task Create_under_a_parent_belonging_to_another_owner_fails()
    {
        var tenantId = Guid.NewGuid();
        var parent = RootFolder(tenantId); // OwnerType.Tenant
        var folders = new FakeFolderRepository();
        folders.Seed(parent);

        var result = await CreateFolderHandler.Handle(
            new CreateFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                parent.Id,
                "Recibos",
                OwnerType.Customer,
                Guid.NewGuid()
            ),
            folders,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.OwnerMismatch, result.Error);
    }

    [Fact]
    public async Task Rename_cascades_the_new_path_to_descendants()
    {
        var tenantId = Guid.NewGuid();
        var root = RootFolder(tenantId, "Viejo");
        var child = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                root.Id,
                FolderName.Create("Hijo").Value,
                root.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);

        var result = await RenameFolderHandler.Handle(
            new RenameFolderCommand(tenantId, Guid.NewGuid(), TenantScope, root.Id, "Nuevo"),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("/Nuevo", root.RelativePath);
        Assert.Equal("/Nuevo/Hijo", child.RelativePath); // cascadeo al descendiente
    }

    [Fact]
    public async Task Rename_of_a_folder_belonging_to_another_tenant_is_not_found()
    {
        var root = RootFolder(Guid.NewGuid());
        var folders = new FakeFolderRepository();
        folders.Seed(root);

        var result = await RenameFolderHandler.Handle(
            new RenameFolderCommand(Guid.NewGuid(), Guid.NewGuid(), TenantScope, root.Id, "Nuevo"),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Move_cascades_the_new_path_to_descendants()
    {
        var tenantId = Guid.NewGuid();
        var oldParent = RootFolder(tenantId, "Origen");
        var newParent = RootFolder(tenantId, "Destino");
        var moving = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                oldParent.Id,
                FolderName.Create("Carpeta").Value,
                oldParent.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var grandchild = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                moving.Id,
                FolderName.Create("Nieto").Value,
                moving.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var folders = new FakeFolderRepository();
        folders.Seed(oldParent);
        folders.Seed(newParent);
        folders.Seed(moving);
        folders.Seed(grandchild);

        var result = await MoveFolderHandler.Handle(
            new MoveFolderCommand(tenantId, Guid.NewGuid(), TenantScope, moving.Id, newParent.Id),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("/Destino/Carpeta", moving.RelativePath);
        Assert.Equal("/Destino/Carpeta/Nieto", grandchild.RelativePath);
    }

    [Fact]
    public async Task Move_into_its_own_descendant_is_rejected_as_circular()
    {
        var tenantId = Guid.NewGuid();
        var parent = RootFolder(tenantId, "Padre");
        var child = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                parent.Id,
                FolderName.Create("Hijo").Value,
                parent.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var folders = new FakeFolderRepository();
        folders.Seed(parent);
        folders.Seed(child);

        var result = await MoveFolderHandler.Handle(
            new MoveFolderCommand(tenantId, Guid.NewGuid(), TenantScope, parent.Id, child.Id),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.CircularReference, result.Error);
    }

    [Fact]
    public async Task MoveFileToFolder_succeeds_when_the_target_folder_shares_the_file_s_owner()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

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
            new FakeShareLinkRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(folder.Id, file.FolderId);
    }

    [Fact]
    public async Task MoveFileToFolder_rejects_a_folder_owned_by_someone_else()
    {
        var tenantId = Guid.NewGuid();
        var customerFolder = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                Guid.NewGuid(),
                null,
                FolderName.Create("DeOtroCliente").Value,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var file = RegisteredFile(tenantId); // OwnerType.Tenant
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var folders = new FakeFolderRepository();
        folders.Seed(customerFolder);

        var result = await MoveFileToFolderHandler.Handle(
            new MoveFileToFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                file.Id,
                customerFolder.Id,
                new RequestAuditContext(null, null, "corr-1")
            ),
            files,
            folders,
            new FakeShareLinkRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeMessageBus(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.OwnerMismatch, result.Error);
    }

    [Fact]
    public async Task GetFolderContents_only_returns_the_caller_tenant_s_subfolders_and_files()
    {
        var tenantId = Guid.NewGuid();
        var ownFolder = RootFolder(tenantId);
        var otherTenantFolder = RootFolder(Guid.NewGuid());
        var ownFile = RegisteredFile(tenantId);
        var folders = new FakeFolderRepository();
        folders.Seed(ownFolder);
        folders.Seed(otherTenantFolder);
        var files = new FakeFileObjectRepository();
        files.Seed(ownFile);

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null),
            folders,
            files,
            CancellationToken.None
        );

        var subfolder = Assert.Single(result.Subfolders);
        Assert.Equal(ownFolder.Id, subfolder.Id);
        var fileResponse = Assert.Single(result.Files);
        Assert.Equal(ownFile.Id, fileResponse.Id);
    }

    private static FileObject RegisteredFile(Guid tenantId)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
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
    }
}
