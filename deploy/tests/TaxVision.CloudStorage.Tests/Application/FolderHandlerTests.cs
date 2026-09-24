using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;

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
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        var subfolder = Assert.Single(result.Subfolders);
        Assert.Equal(ownFolder.Id, subfolder.Id);
        Assert.False(subfolder.IsShared);
        var fileResponse = Assert.Single(result.Files);
        Assert.Equal(ownFile.Id, fileResponse.Id);
        Assert.False(fileResponse.IsShared);
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
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        var subfolder = Assert.Single(result.Subfolders);
        Assert.Equal(folderA.Id, subfolder.Id);
    }

    [Fact]
    public async Task GetFolderContents_paginates_folders_first_and_reports_totals()
    {
        var tenantId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        for (var i = 0; i < 3; i++)
            folders.Seed(RootFolder(tenantId, $"F{i}"));
        var files = new FakeFileObjectRepository();
        for (var i = 0; i < 4; i++)
            files.Seed(RegisteredFile(tenantId));

        // Página 1: take=2 → 2 carpetas (carpetas primero), 0 archivos.
        var page1 = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null, null, null, 0, 2),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );
        Assert.Equal(2, page1.Subfolders.Count);
        Assert.Empty(page1.Files);
        Assert.Equal(3, page1.FolderCount);
        Assert.Equal(4, page1.FileCount);
        Assert.Equal(7, page1.TotalCount);

        // Página 2 (skip=2, take=2): la última carpeta + el primer archivo (cruce del límite).
        var page2 = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null, null, null, 2, 2),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );
        Assert.Single(page2.Subfolders);
        Assert.Single(page2.Files);

        // Última página (skip=6, take=2): solo el archivo restante.
        var page4 = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null, null, null, 6, 2),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );
        Assert.Empty(page4.Subfolders);
        Assert.Single(page4.Files);
    }

    [Fact]
    public async Task GetFolderContents_with_a_file_filter_hides_folders_and_applies_the_filter()
    {
        var tenantId = Guid.NewGuid();
        var folders = new FakeFolderRepository();
        folders.Seed(RootFolder(tenantId, "Una carpeta"));
        var files = new FakeFileObjectRepository();
        files.Seed(FileWithName(tenantId, "a.pdf", 2024));
        files.Seed(FileWithName(tenantId, "b.xlsx", 2024));
        files.Seed(FileWithName(tenantId, "c.pdf", 2023));

        // Filtro: solo PDF de 2024 → 1 archivo, y las carpetas se ocultan.
        var filter = new FolderContentsFilter(TaxYears: [2024], Extensions: ["PDF"], SortKey: FileSortKey.Name);
        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null, null, null, 0, 25, filter),
            folders,
            files,
            new FakeShareLinkRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            CancellationToken.None
        );

        Assert.Empty(result.Subfolders); // ocultas por el file-filter
        Assert.Equal(0, result.FolderCount);
        var only = Assert.Single(result.Files);
        Assert.Equal("a.pdf", only.OriginalName);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetFolderContents_marks_files_and_folders_that_have_an_active_share()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var sharedFolder = RootFolder(tenantId, "Compartida");
        var plainFolder = RootFolder(tenantId, "Normal");
        var sharedFile = RegisteredFile(tenantId);
        var plainFile = RegisteredFile(tenantId);
        var folders = new FakeFolderRepository();
        folders.Seed(sharedFolder);
        folders.Seed(plainFolder);
        var files = new FakeFileObjectRepository();
        files.Seed(sharedFile);
        files.Seed(plainFile);

        var shares = new FakeShareLinkRepository();
        shares.Seed(
            ShareLink
                .Create(
                    Guid.NewGuid(),
                    tenantId,
                    sharedFolder.Id,
                    ShareResourceType.Folder,
                    ShareVisibility.ExternalLink,
                    SharePermission.Download,
                    null,
                    null,
                    null,
                    Guid.NewGuid(),
                    now
                )
                .Value.Item1
        );
        shares.Seed(
            ShareLink
                .Create(
                    Guid.NewGuid(),
                    tenantId,
                    sharedFile.Id,
                    ShareResourceType.File,
                    ShareVisibility.ExternalLink,
                    SharePermission.View,
                    null,
                    null,
                    null,
                    Guid.NewGuid(),
                    now
                )
                .Value.Item1
        );

        var result = await GetFolderContentsHandler.Handle(
            new GetFolderContentsQuery(tenantId, TenantScope, null),
            folders,
            files,
            shares,
            new FakeSystemClock(now),
            CancellationToken.None
        );

        Assert.True(result.Subfolders.Single(f => f.Id == sharedFolder.Id).IsShared);
        Assert.False(result.Subfolders.Single(f => f.Id == plainFolder.Id).IsShared);
        Assert.True(result.Files.Single(f => f.Id == sharedFile.Id).IsShared);
        Assert.False(result.Files.Single(f => f.Id == plainFile.Id).IsShared);
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

    /// <summary>Invoca DeleteFolderHandler con los fakes por defecto (papelera recursiva).</summary>
    private static Task<Result> DeleteFolder(
        Guid tenantId,
        Guid folderId,
        FakeFolderRepository folders,
        FakeFileObjectRepository files,
        FakeUnitOfWork unitOfWork,
        StorageActorScope? scope = null
    ) =>
        DeleteFolderHandler.Handle(
            new DeleteFolderCommand(
                tenantId,
                Guid.NewGuid(),
                scope ?? TenantScope,
                folderId,
                new RequestAuditContext(null, null, "corr-1")
            ),
            folders,
            files,
            new FakeStorageAuditRepository(),
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

    private static Folder ChildFolder(Guid tenantId, Folder parent, string name = "Hijo") =>
        Folder
            .Create(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                parent.Id,
                FolderName.Create(name).Value,
                parent.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task DeleteFolder_sends_an_empty_folder_to_the_recycle_bin()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var unitOfWork = new FakeUnitOfWork();

        var result = await DeleteFolder(tenantId, folder.Id, folders, new FakeFileObjectRepository(), unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.True(folder.IsDeleted); // a la papelera, no borrada en duro
        Assert.Equal(folder.Id, folder.DeletedBatchId); // es la raíz del batch
        // Ya no aparece navegando.
        Assert.Empty(
            await folders.ListSubfoldersAsync(
                tenantId,
                null,
                null,
                null,
                null,
                FolderContentsFilter.None,
                null,
                null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task DeleteFolder_recursively_sends_the_whole_subtree_to_the_recycle_bin()
    {
        var tenantId = Guid.NewGuid();
        var parent = RootFolder(tenantId, "Padre");
        var child = ChildFolder(tenantId, parent);
        var folders = new FakeFolderRepository();
        folders.Seed(parent);
        folders.Seed(child);

        var result = await DeleteFolder(
            tenantId,
            parent.Id,
            folders,
            new FakeFileObjectRepository(),
            new FakeUnitOfWork()
        );

        Assert.True(result.IsSuccess);
        Assert.True(parent.IsDeleted);
        Assert.True(child.IsDeleted);
        // Ambas comparten el batch de la raíz.
        Assert.Equal(parent.Id, parent.DeletedBatchId);
        Assert.Equal(parent.Id, child.DeletedBatchId);
    }

    [Fact]
    public async Task DeleteFolder_recursively_sends_files_to_the_recycle_bin_keeping_their_folder()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(folder.Id, DateTime.UtcNow);
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await DeleteFolder(tenantId, folder.Id, folders, files, new FakeUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.True(folder.IsDeleted);
        Assert.Equal(FileStatus.SoftDeleted, file.Status); // a la papelera, no se perdió
        Assert.Equal(folder.Id, file.DeletedBatchId); // agrupado con su carpeta
        Assert.Equal(folder.Id, file.FolderId); // conserva su carpeta → al restaurar vuelve a su sitio
    }

    [Fact]
    public async Task DeleteFolder_with_a_legally_held_file_fails_without_deleting_anything()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId);
        var file = RegisteredFile(tenantId);
        file.MoveToFolder(folder.Id, DateTime.UtcNow);
        file.PlaceLegalHold();
        var folders = new FakeFolderRepository();
        folders.Seed(folder);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await DeleteFolder(tenantId, folder.Id, folders, files, new FakeUnitOfWork());

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.HasLegalHold, result.Error);
        Assert.NotNull(await folders.GetAsync(tenantId, folder.Id, CancellationToken.None)); // no se borró
        Assert.NotEqual(FileStatus.SoftDeleted, file.Status);
    }

    [Fact]
    public async Task DeleteFolder_of_a_folder_belonging_to_another_tenant_is_not_found()
    {
        var folder = RootFolder(Guid.NewGuid());
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await DeleteFolder(
            Guid.NewGuid(),
            folder.Id,
            folders,
            new FakeFileObjectRepository(),
            new FakeUnitOfWork()
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

    private static FileObject FileWithName(Guid tenantId, string originalName, int taxYear)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/{taxYear}/{Guid.NewGuid():N}").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                taxYear,
                key,
                originalName,
                "application/octet-stream",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
    }

    // ---------- Proteccion de carpetas de sistema (sys.*) ----------

    private static Folder SystemFolder(Guid tenantId, string category = "sys.documents", string name = "Documents") =>
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
                DateTime.UtcNow,
                FolderCategory.Create(category).Value
            )
            .Value;

    [Fact]
    public async Task Rename_rejects_a_system_folder()
    {
        var tenantId = Guid.NewGuid();
        var folder = SystemFolder(tenantId, "sys.signatures", "Signed Documents");
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await RenameFolderHandler.Handle(
            new RenameFolderCommand(tenantId, Guid.NewGuid(), TenantScope, folder.Id, "Mis firmas"),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
    }

    [Fact]
    public async Task Delete_rejects_a_system_folder()
    {
        var tenantId = Guid.NewGuid();
        var folder = SystemFolder(tenantId, "sys.email", "Email");
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await DeleteFolder(
            tenantId,
            folder.Id,
            folders,
            new FakeFileObjectRepository(),
            new FakeUnitOfWork()
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
    }

    [Fact]
    public async Task Move_rejects_a_system_folder()
    {
        var tenantId = Guid.NewGuid();
        var system = SystemFolder(tenantId, "sys.documents", "Documents");
        var target = RootFolder(tenantId, "Destino");
        var folders = new FakeFolderRepository();
        folders.Seed(system);
        folders.Seed(target);

        var result = await MoveFolderHandler.Handle(
            new MoveFolderCommand(tenantId, Guid.NewGuid(), TenantScope, system.Id, target.Id),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
    }

    [Fact]
    public async Task Rename_allows_a_user_folder()
    {
        var tenantId = Guid.NewGuid();
        var folder = RootFolder(tenantId, "Clientes");
        var folders = new FakeFolderRepository();
        folders.Seed(folder);

        var result = await RenameFolderHandler.Handle(
            new RenameFolderCommand(tenantId, Guid.NewGuid(), TenantScope, folder.Id, "Prospectos"),
            folders,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_rejects_a_system_category()
    {
        var tenantId = Guid.NewGuid();

        var result = await CreateFolderHandler.Handle(
            new CreateFolderCommand(
                tenantId,
                Guid.NewGuid(),
                TenantScope,
                null,
                "Email",
                OwnerType.Tenant,
                null,
                "sys.email"
            ),
            new FakeFolderRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            NoOpLogger,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
    }
}
