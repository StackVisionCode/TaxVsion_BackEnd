using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Fase C2 — CreateFolderHandler, RenameFolderHandler, MoveFolderHandler,
/// MoveFileToFolderHandler, GetFolderContentsHandler. 2026-07-20: Category get-or-create,
/// unicidad de nombre scopeada por dueno, GetFolderTreeHandler.
/// </summary>
public sealed class FolderHandlerTests
{
    private static readonly StorageActorScope TenantScope = new(false, null);
    private static readonly NullLogger<Folder> NoOpLogger = NullLogger<Folder>.Instance;

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
            NoOpLogger,
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
            NoOpLogger,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NameAlreadyExists, result.Error);
    }

    [Fact]
    public async Task Create_allows_the_same_name_for_two_different_owners()
    {
        // 2026-07-20 — regresion del gap real: antes NameExistsUnderParentAsync solo miraba
        // (TenantId, ParentFolderId, Name), asi que dos clientes distintos no podian tener
        // cada uno un folder raiz "Documentos" sin chocar entre si.
        var tenantId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        folders.Seed(
            Folder
                .Create(
                    Guid.NewGuid(),
                    tenantId,
                    OwnerType.Customer,
                    Guid.NewGuid(),
                    null,
                    FolderName.Create("Documentos").Value,
                    null,
                    Guid.NewGuid(),
                    DateTime.UtcNow
                )
                .Value
        );

        var result = await CreateFolderHandler.Handle(
            new CreateFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                null,
                "Documentos",
                OwnerType.Customer,
                Guid.NewGuid() // otro cliente distinto
            ),
            folders,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            NoOpLogger,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_with_category_is_idempotent_get_or_create_on_a_concurrent_race()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var winner = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerId,
                null,
                FolderName.Create("Documentos").Value,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                FolderCategory.Create("customer.documents").Value
            )
            .Value;
        var folders = new FakeFolderRepository { SimulateAddNeverPersists = true };
        folders.Seed(winner);
        // El pre-check de arriba NO ve al ganador porque el nombre elegido por el perdedor
        // es distinto ("Docs" vs "Documentos") — asi se aisla que el fallback es el que
        // realmente resuelve la carrera, no el pre-check.
        var unitOfWork = new FakeUnitOfWork { ThrowConflictOnNextSave = true };

        var result = await CreateFolderHandler.Handle(
            new CreateFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                null,
                "Docs",
                OwnerType.Customer,
                customerId,
                "customer.documents"
            ),
            folders,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            NoOpLogger,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.Value.Id); // devuelve al ganador de la carrera, no crea un duplicado
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
            NoOpLogger,
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

    [Fact]
    public async Task GetFolderContents_filters_by_owner_when_requested_by_staff()
    {
        // 2026-07-20 — cierra el gap de "dame solo el arbol de este cliente": sin
        // ownerType/ownerId, staff veia TODOS los duenos del tenant mezclados en la raiz.
        var tenantId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var folderA = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerAId,
                null,
                FolderName.Create("DeA").Value,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var folderB = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerBId,
                null,
                FolderName.Create("DeB").Value,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var folders = new FakeFolderRepository();
        folders.Seed(folderA);
        folders.Seed(folderB);

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null, OwnerType.Customer, customerAId),
            folders,
            new FakeFileObjectRepository(),
            CancellationToken.None
        );

        var subfolder = Assert.Single(result.Subfolders);
        Assert.Equal(folderA.Id, subfolder.Id);
    }

    [Fact]
    public async Task GetFolderTree_builds_the_full_nested_tree_in_one_call()
    {
        var tenantId = Guid.NewGuid();
        var root = RootFolder(tenantId, "Raiz");
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
        var grandchild = Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                child.Id,
                FolderName.Create("Nieto").Value,
                child.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var folders = new FakeFolderRepository();
        folders.Seed(root);
        folders.Seed(child);
        folders.Seed(grandchild);

        var result = await GetFolderTreeHandler.Handle(
            new GetFolderTreeQuery(tenantId, TenantScope),
            folders,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var rootNode = Assert.Single(result.Value);
        Assert.Equal(root.Id, rootNode.Id);
        var childNode = Assert.Single(rootNode.Children);
        Assert.Equal(child.Id, childNode.Id);
        var grandchildNode = Assert.Single(childNode.Children);
        Assert.Equal(grandchild.Id, grandchildNode.Id);
    }

    [Fact]
    public async Task GetFolderTree_customer_portal_cannot_request_another_owner()
    {
        var tenantId = Guid.NewGuid();
        var ownCustomerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var customerScope = new StorageActorScope(true, ownCustomerId);
        var folders = new FakeFolderRepository();

        var result = await GetFolderTreeHandler.Handle(
            new GetFolderTreeQuery(tenantId, customerScope, OwnerType.Customer, otherCustomerId),
            folders,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task DeleteFolder_removes_an_empty_folder()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var unitOfWork = new FakeUnitOfWork();

        var result = await DeleteFolderHandler.Handle(
            new DeleteFolderCommand(tenantId, Guid.NewGuid(), TenantScope, folder.Id),
            folders,
            new FakeFileObjectRepository(),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Null(await folders.GetAsync(tenantId, folder.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteFolder_rejects_a_folder_that_still_has_a_subfolder()
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

        var result = await DeleteFolderHandler.Handle(
            new DeleteFolderCommand(tenantId, Guid.NewGuid(), TenantScope, parent.Id),
            folders,
            new FakeFileObjectRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotEmpty, result.Error);
        Assert.NotNull(await folders.GetAsync(tenantId, parent.Id, CancellationToken.None)); // no se borró
    }

    [Fact]
    public async Task DeleteFolder_rejects_a_folder_that_still_has_a_file()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(folder.Id, DateTime.UtcNow);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await DeleteFolderHandler.Handle(
            new DeleteFolderCommand(tenantId, Guid.NewGuid(), TenantScope, folder.Id),
            folders,
            files,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotEmpty, result.Error);
    }

    [Fact]
    public async Task DeleteFolder_of_a_folder_belonging_to_another_tenant_is_not_found()
    {
        var folder = RootFolder(Guid.NewGuid());
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await DeleteFolderHandler.Handle(
            new DeleteFolderCommand(Guid.NewGuid(), Guid.NewGuid(), TenantScope, folder.Id),
            folders,
            new FakeFileObjectRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotFound, result.Error);
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
