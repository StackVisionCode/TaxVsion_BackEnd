using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase B2/B2.1 — PrepareZipDownloadHandler: valida y arma el plan de una descarga ZIP (archivos + carpetas).</summary>
public sealed class ZipDownloadHandlerTests
{
    private static FileObject AvailableFile(
        Guid tenantId,
        string originalName = "report.pdf",
        long sizeBytes = 10,
        Guid? folderId = null
    )
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
                originalName,
                "application/pdf",
                sizeBytes,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        if (folderId is { } id)
            file.MoveToFolder(id, DateTime.UtcNow);
        return file;
    }

    private static Folder RootFolder(Guid tenantId, string name = "Recibos") =>
        Folder
            .Create(Guid.NewGuid(), tenantId, OwnerType.Tenant, null, null, FolderName.Create(name).Value, null, Guid.NewGuid(), DateTime.UtcNow)
            .Value;

    private static Folder ChildFolder(Guid tenantId, Folder parent, string name) =>
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

    private static RequestAuditContext Audit() => new(null, null, "corr-1");

    private static IOptions<CloudStorageOptions> Options(
        int maxFiles = 500,
        long maxBytes = 500L * 1024 * 1024,
        int maxFolders = 50
    ) =>
        Microsoft.Extensions.Options.Options.Create(
            new CloudStorageOptions { MaxZipFiles = maxFiles, MaxZipAggregateBytes = maxBytes, MaxZipFolders = maxFolders }
        );

    private sealed record Harness(
        FakeFileObjectRepository Files,
        FakeFolderRepository Folders,
        FakeStorageAuditRepository Audit,
        FakeUnitOfWork UnitOfWork,
        FakeMessageBus Bus
    )
    {
        public static Harness New() => new(new(), new(), new(), new(), new());

        public Task<BuildingBlocks.Results.Result<ZipDownloadPlan>> Handle(
            PrepareZipDownloadQuery query,
            IOptions<CloudStorageOptions>? options = null
        ) =>
            PrepareZipDownloadHandler.Handle(
                query,
                Files,
                Folders,
                Audit,
                options ?? Options(),
                new FakeSystemClock(DateTime.UtcNow),
                UnitOfWork,
                Bus,
                CancellationToken.None
            );
    }

    [Fact]
    public async Task Two_available_files_produce_a_plan_and_audit_the_batch()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var fileA = AvailableFile(tenantId, "a.pdf", 100);
        var fileB = AvailableFile(tenantId, "b.pdf", 200);
        h.Files.Seed(fileA);
        h.Files.Seed(fileB);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [fileA.Id, fileB.Id], [], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.Equal(fileA.Id, result.Value.Entries[0].FileId);
        Assert.Equal(fileB.Id, result.Value.Entries[1].FileId);
        Assert.Single(h.Audit.Logs, log => log.Action == "download.zip" && log.Details == "files=2;folders=0;bytes=300");
        Assert.Equal(2, h.Bus.Published.OfType<FileAccessAuditedIntegrationEvent>().Count(e => e.Action == "download.zip"));
        Assert.Equal(1, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Duplicate_file_names_get_a_numeric_suffix_in_request_order()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var first = AvailableFile(tenantId, "report.pdf");
        var second = AvailableFile(tenantId, "report.pdf");
        h.Files.Seed(first);
        h.Files.Seed(second);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [first.Id, second.Id], [], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("report.pdf", result.Value.Entries[0].EntryName);
        Assert.Equal("report_1.pdf", result.Value.Entries[1].EntryName);
    }

    [Fact]
    public async Task Empty_request_fails_without_auditing_or_saving()
    {
        var h = Harness.New();

        var result = await h.Handle(
            new PrepareZipDownloadQuery(Guid.NewGuid(), Guid.NewGuid(), new StorageActorScope(false, null), [], [], Audit())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NoFilesRequested, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Duplicate_ids_in_the_request_are_deduplicated_to_a_single_entry()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var file = AvailableFile(tenantId);
        h.Files.Seed(file);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [file.Id, file.Id], [], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Entries);
        Assert.Single(h.Bus.Published.OfType<FileAccessAuditedIntegrationEvent>());
    }

    [Fact]
    public async Task Requesting_more_files_than_the_cap_fails_with_TooManyItems()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var file = AvailableFile(tenantId, $"f{i}.pdf");
            h.Files.Seed(file);
            ids.Add(file.Id);
        }

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), ids, [], Audit()),
            Options(maxFiles: 2)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.TooManyItems, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_missing_or_cross_tenant_file_fails_the_whole_batch_with_NotFound()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var owned = AvailableFile(tenantId);
        h.Files.Seed(owned);
        var otherTenantsFile = AvailableFile(Guid.NewGuid());
        h.Files.Seed(otherTenantsFile);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [owned.Id, otherTenantsFile.Id],
                [],
                Audit()
            )
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_file_that_has_not_passed_the_scan_fails_the_whole_batch_with_NotAvailable()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var pendingFile = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "pending.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value; // se queda en PendingUpload, nunca escaneado
        h.Files.Seed(pendingFile);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [pendingFile.Id], [], Audit())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotAvailable, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Aggregate_size_over_the_cap_fails_with_ZipTooLarge()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var fileA = AvailableFile(tenantId, "a.pdf", 300);
        var fileB = AvailableFile(tenantId, "b.pdf", 300);
        h.Files.Seed(fileA);
        h.Files.Seed(fileB);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [fileA.Id, fileB.Id], [], Audit()),
            Options(maxBytes: 500)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ZipTooLarge, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    // ---- Fase B2.1 — descarga de carpetas ----

    [Fact]
    public async Task A_requested_folder_pulls_in_its_files_and_its_subfolders_files_with_path_prefixes()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var root = RootFolder(tenantId, "Recibos");
        var sub = ChildFolder(tenantId, root, "2025");
        h.Folders.Seed(root);
        h.Folders.Seed(sub);
        var rootFile = AvailableFile(tenantId, "top.pdf", 10, root.Id);
        var subFile = AvailableFile(tenantId, "receipt.pdf", 20, sub.Id);
        h.Files.Seed(rootFile);
        h.Files.Seed(subFile);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [], [root.Id], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.Contains(result.Value.Entries, e => e.FileId == rootFile.Id && e.EntryName == "Recibos/top.pdf");
        Assert.Contains(result.Value.Entries, e => e.FileId == subFile.Id && e.EntryName == "Recibos/2025/receipt.pdf");
    }

    [Fact]
    public async Task An_empty_folder_with_no_explicit_files_fails_with_NoFilesResolved()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var root = RootFolder(tenantId);
        h.Folders.Seed(root);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [], [root.Id], Audit())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NoFilesResolved, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_file_still_scanning_inside_a_folder_is_skipped_without_failing_the_rest()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var root = RootFolder(tenantId);
        h.Folders.Seed(root);
        var available = AvailableFile(tenantId, "ready.pdf", 10, root.Id);
        h.Files.Seed(available);

        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var stillScanning = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "scanning.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        stillScanning.MoveToFolder(root.Id, DateTime.UtcNow); // PendingUpload — nunca llega a Available
        h.Files.Seed(stillScanning);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [], [root.Id], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Entries);
        Assert.Equal(available.Id, result.Value.Entries[0].FileId);
    }

    [Fact]
    public async Task A_missing_or_cross_tenant_folder_fails_with_FolderNotFound()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [], [Guid.NewGuid()], Audit())
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Two_root_folders_with_the_same_name_get_disambiguated_top_level_prefixes()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var rootA = RootFolder(tenantId, "Recibos");
        var rootB = Folder
            .Create(Guid.NewGuid(), tenantId, OwnerType.Tenant, null, null, FolderName.Create("Recibos").Value, null, Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        h.Folders.Seed(rootA);
        h.Folders.Seed(rootB);
        var fileA = AvailableFile(tenantId, "a.pdf", 10, rootA.Id);
        var fileB = AvailableFile(tenantId, "b.pdf", 10, rootB.Id);
        h.Files.Seed(fileA);
        h.Files.Seed(fileB);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [], [rootA.Id, rootB.Id], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value.Entries, e => e.EntryName == "Recibos/a.pdf");
        Assert.Contains(result.Value.Entries, e => e.EntryName == "Recibos_1/b.pdf");
    }

    [Fact]
    public async Task A_file_explicitly_requested_by_id_is_not_duplicated_when_its_folder_is_also_requested()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var root = RootFolder(tenantId);
        h.Folders.Seed(root);
        var file = AvailableFile(tenantId, "shared.pdf", 10, root.Id);
        h.Files.Seed(file);

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [file.Id], [root.Id], Audit())
        );

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Entries);
        // Ganó la resolución estricta (FileIds): va suelto en la raíz, no bajo el prefijo de la carpeta.
        Assert.Equal("shared.pdf", result.Value.Entries[0].EntryName);
    }

    [Fact]
    public async Task Requesting_more_folders_than_the_cap_fails_with_TooManyFolders()
    {
        var tenantId = Guid.NewGuid();
        var h = Harness.New();
        var folderIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var folder = RootFolder(tenantId, $"F{i}");
            h.Folders.Seed(folder);
            folderIds.Add(folder.Id);
        }

        var result = await h.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), [], folderIds, Audit()),
            Options(maxFolders: 2)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.TooManyFolders, result.Error);
        Assert.Empty(h.Audit.Logs);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }
}
