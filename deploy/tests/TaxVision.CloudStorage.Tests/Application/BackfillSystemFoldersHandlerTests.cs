using TaxVision.CloudStorage.Application.Administration;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Fase 3 — BackfillSystemFoldersHandler: dry-run reporta sin mutar; apply archiva los
/// FolderId=null navegables; idempotente; barrido multi-tenant cuando TenantId es null.
/// </summary>
public sealed class BackfillSystemFoldersHandlerTests
{
    private static FileObject Unfiled(Guid tenantId, OwnerType ownerType, Guid? ownerId, FolderType folderType)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/{ownerType}/x/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                ownerType,
                ownerId,
                folderType,
                folderType == FolderType.Branding ? null : 2025,
                key,
                "doc.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;
    }

    [Fact]
    public async Task Dry_run_reports_groups_without_filing_anything()
    {
        var tenantId = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.Documents));
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.Documents));
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.EmailIncoming));
        var provisioner = new SystemFolderProvisioner(new FakeFolderRepository());

        var result = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(tenantId, DryRun: true, BatchSize: 200),
            files,
            provisioner,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.DryRun);
        Assert.Equal(3, result.Value.FilesFiled);
        Assert.Equal(1, result.Value.TenantsProcessed);
        // Nada mutado: siguen sin carpeta.
        Assert.All(files.All(), f => Assert.Null(f.FolderId));
    }

    [Fact]
    public async Task Apply_files_navigable_files_into_their_system_folder()
    {
        var tenantId = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.Documents));
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.Documents));
        var folders = new FakeFolderRepository();
        var provisioner = new SystemFolderProvisioner(folders);

        var result = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(tenantId, DryRun: false, BatchSize: 200),
            files,
            provisioner,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(2, result.Value.FilesFiled);
        var folder = await folders.GetByOwnerAndCategoryAsync(
            tenantId,
            OwnerType.Customer,
            customer,
            "sys.documents",
            CancellationToken.None
        );
        Assert.NotNull(folder);
        Assert.All(files.All(), f => Assert.Equal(folder!.Id, f.FolderId));
    }

    [Fact]
    public async Task Apply_is_idempotent_second_run_files_nothing()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(Unfiled(tenantId, OwnerType.Customer, Guid.NewGuid(), FolderType.Signatures));
        var provisioner = new SystemFolderProvisioner(new FakeFolderRepository());
        var clock = new FakeSystemClock(DateTime.UtcNow);

        var first = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(tenantId, DryRun: false, BatchSize: 200),
            files,
            provisioner,
            clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        var second = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(tenantId, DryRun: false, BatchSize: 200),
            files,
            provisioner,
            clock,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(1, first.Value.FilesFiled);
        Assert.Equal(0, second.Value.FilesFiled);
    }

    [Fact]
    public async Task Null_tenant_sweeps_every_tenant_with_unfiled_files()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(Unfiled(tenantA, OwnerType.Customer, Guid.NewGuid(), FolderType.Documents));
        files.Seed(Unfiled(tenantB, OwnerType.Tenant, null, FolderType.Documents));
        var provisioner = new SystemFolderProvisioner(new FakeFolderRepository());

        var result = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(TenantId: null, DryRun: false, BatchSize: 200),
            files,
            provisioner,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(2, result.Value.TenantsProcessed);
        Assert.Equal(2, result.Value.FilesFiled);
        Assert.All(files.All(), f => Assert.NotNull(f.FolderId));
    }

    [Fact]
    public async Task Email_incoming_and_outgoing_of_same_customer_share_one_folder()
    {
        var tenantId = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.EmailIncoming));
        files.Seed(Unfiled(tenantId, OwnerType.Customer, customer, FolderType.EmailOutgoing));
        var folders = new FakeFolderRepository();
        var provisioner = new SystemFolderProvisioner(folders);

        var result = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(tenantId, DryRun: false, BatchSize: 200),
            files,
            provisioner,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(2, result.Value.FilesFiled);
        // Ambos tipos comparten la carpeta "Email" (sys.email): una sola, no dos.
        var folder = await folders.GetByOwnerAndCategoryAsync(
            tenantId,
            OwnerType.Customer,
            customer,
            "sys.email",
            CancellationToken.None
        );
        Assert.NotNull(folder);
        var folderIds = files.All().Select(f => f.FolderId).Distinct().ToList();
        Assert.Single(folderIds);
        Assert.Equal(folder!.Id, folderIds[0]);
    }

    [Fact]
    public async Task Internal_folder_types_are_never_filed()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(Unfiled(tenantId, OwnerType.Tenant, null, FolderType.Branding));
        var provisioner = new SystemFolderProvisioner(new FakeFolderRepository());

        var result = await BackfillSystemFoldersHandler.Handle(
            new BackfillSystemFoldersCommand(TenantId: null, DryRun: false, BatchSize: 200),
            files,
            provisioner,
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(0, result.Value.TenantsProcessed);
        Assert.Equal(0, result.Value.FilesFiled);
        Assert.All(files.All(), f => Assert.Null(f.FolderId));
    }
}
