using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class CopyFileHandlerTests
{
    [Fact]
    public async Task Copy_creates_a_distinct_file_and_enqueues_its_scan()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var source = AvailableClientFormsFile(tenantId, customerId);
        var files = new FakeFileObjectRepository();
        files.Seed(source);
        var folders = new FakeFolderRepository();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", 100_000_000, 100_000_000));
        var storage = new FakeObjectStorage();
        var bus = new FakeMessageBus();

        var result = await CopyFileHandler.Handle(
            new CopyFileCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                source.Id,
                new CopyFileRequest(OwnerType.Customer, customerId, FolderType.Documents, 2026),
                new RequestAuditContext(null, null, "corr-copy")
            ),
            files,
            folders,
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            new SystemFolderProvisioner(folders),
            storage,
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(source.Id, result.Value.FileId);
        var copy = await files.GetAsync(tenantId, result.Value.FileId, CancellationToken.None);
        Assert.NotNull(copy);
        Assert.Equal(FileStatus.PendingScan, copy!.Status);
        Assert.Equal(FolderType.Documents, copy.FolderType);
        Assert.Equal(2026, copy.TaxYear);
        Assert.NotEqual(source.ObjectKey, copy.ObjectKey);
        Assert.Single(
            storage.Copied,
            item =>
                item.SourceObjectKey == source.ObjectKey
                && item.DestinationObjectKey == copy.ObjectKey
                && item.SourceBucket == "taxvision-storage"
                && item.DestinationBucket == "taxvision-temp"
        );
        Assert.Single(bus.Published.OfType<ScanFileCommand>(), command => command.FileId == copy.Id);

        var folder = await folders.GetByOwnerAndCategoryAsync(
            tenantId,
            OwnerType.Customer,
            customerId,
            "sys.documents",
            CancellationToken.None
        );
        Assert.Equal(folder!.Id, copy.FolderId);
    }

    [Fact]
    public async Task Copy_into_client_forms_folder_type_is_rejected_without_side_effects()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var source = AvailableClientFormsFile(tenantId, customerId);
        var files = new FakeFileObjectRepository();
        files.Seed(source);
        var folders = new FakeFolderRepository();
        var storage = new FakeObjectStorage();
        var bus = new FakeMessageBus();

        var result = await CopyAsync(
            tenantId,
            source.Id,
            new CopyFileRequest(OwnerType.Customer, customerId, FolderType.ClientForms, null),
            files,
            folders,
            storage,
            bus
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ClientFormsDestinationNotAllowed, result.Error);
        Assert.Empty(storage.Copied);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Copy_into_the_client_forms_system_folder_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var source = AvailableClientFormsFile(tenantId, customerId);
        var files = new FakeFileObjectRepository();
        files.Seed(source);
        var folders = new FakeFolderRepository();
        var clientFormsFolderId = await new SystemFolderProvisioner(folders).ResolveFolderIdAsync(
            tenantId,
            OwnerType.Customer,
            customerId,
            FolderType.ClientForms,
            Guid.NewGuid(),
            DateTime.UtcNow,
            CancellationToken.None
        );
        var storage = new FakeObjectStorage();
        var bus = new FakeMessageBus();

        var result = await CopyAsync(
            tenantId,
            source.Id,
            new CopyFileRequest(OwnerType.Customer, customerId, FolderType.Documents, 2026, clientFormsFolderId),
            files,
            folders,
            storage,
            bus
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ClientFormsDestinationNotAllowed, result.Error);
        Assert.Empty(storage.Copied);
        Assert.Empty(bus.Published);
    }

    private static Task<BuildingBlocks.Results.Result<CopiedFileResponse>> CopyAsync(
        Guid tenantId,
        Guid sourceId,
        CopyFileRequest request,
        FakeFileObjectRepository files,
        FakeFolderRepository folders,
        FakeObjectStorage storage,
        FakeMessageBus bus
    )
    {
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", 100_000_000, 100_000_000));
        return CopyFileHandler.Handle(
            new CopyFileCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                sourceId,
                request,
                new RequestAuditContext(null, null, "corr-copy")
            ),
            files,
            folders,
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            new SystemFolderProvisioner(folders),
            storage,
            Options.Create(new CloudStorageOptions()),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );
    }

    private static FileObject AvailableClientFormsFile(Guid tenantId, Guid customerId)
    {
        var now = DateTime.UtcNow;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerId,
                FolderType.ClientForms,
                null,
                ObjectKey
                    .Create($"tenants/{tenantId:N}/customer/{customerId:N}/client-forms/{Guid.NewGuid():N}.pdf")
                    .Value,
                "form.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan().IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(
            file.MarkAvailable(
                ChecksumSha256.Create("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").Value,
                "application/pdf",
                now
            ).IsSuccess
        );
        return file;
    }
}
