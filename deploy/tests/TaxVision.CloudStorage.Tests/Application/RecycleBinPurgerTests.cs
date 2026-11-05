using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Purga física desde la papelera: el avatar borra todas sus versiones; la cuota y el bucket salen del
/// estado previo al borrado (CST-5).
/// </summary>
public sealed class RecycleBinPurgerTests
{
    private const long Mb = 1024L * 1024;
    private static readonly CloudStorageOptions Options = new();
    private static readonly string MainBucket = Options.MainBucket;

    [Fact]
    public async Task Purging_an_avatar_deletes_every_version_of_the_object()
    {
        var avatar = AvatarTestFiles.Avatar(Guid.NewGuid(), OwnerType.User, Guid.NewGuid());
        var storage = new FakeObjectStorage();

        var purged = await PurgeAsync(avatar, storage);

        Assert.True(purged);
        Assert.Single(storage.DeletedAllVersions, d => d == (MainBucket, avatar.ObjectKey));
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task Purging_a_document_keeps_the_regular_delete_and_its_version_history()
    {
        var document = AvatarTestFiles.Document(Guid.NewGuid(), Guid.NewGuid());
        var storage = new FakeObjectStorage();

        await PurgeAsync(document, storage);

        Assert.Single(storage.Deleted, d => d == (MainBucket, document.ObjectKey));
        Assert.Empty(storage.DeletedAllVersions);
    }

    [Fact]
    public async Task Purge_SoftDeletedInfectedFile_DoesNotReleaseUsedBytes()
    {
        var file = NewDocument(500 * Mb);
        MarkInfected(file);
        var limit = NewLimit(file.TenantId, used: 1024 * Mb, reserved: 0);

        await PurgeAsync(file, new FakeObjectStorage(), limit);

        Assert.Equal(1024 * Mb, limit.UsedBytes);
    }

    [Fact]
    public async Task Purge_SoftDeletedPendingUpload_ReleasesReservation()
    {
        var file = NewDocument(200 * Mb);
        var limit = NewLimit(file.TenantId, used: 300 * Mb, reserved: 200 * Mb);

        await PurgeAsync(file, new FakeObjectStorage(), limit);

        Assert.Equal(0, limit.ReservedBytes);
        Assert.Equal(300 * Mb, limit.UsedBytes);
    }

    [Fact]
    public async Task Purge_SoftDeletedScanFailed_DeletesFromTempBucket()
    {
        var file = NewDocument(4);
        Assert.True(file.RejectUpload("Upload size mismatch.", DateTime.UtcNow).IsSuccess);
        var storage = new FakeObjectStorage();

        await PurgeAsync(file, storage);

        Assert.Equal((Options.TempBucket, file.ObjectKey), Assert.Single(storage.Deleted));
    }

    [Fact]
    public async Task Purge_SoftDeletedInfected_CopiesToPurgedPrefixThenDeletesOriginal()
    {
        // CST-18: la evidencia pasa a purged/ como objeto nuevo, así los 30 días cuentan desde la purga.
        var file = NewDocument(4);
        MarkInfected(file);
        var storage = new FakeObjectStorage();
        storage.Existing.Add((Options.QuarantineBucket, file.ObjectKey));
        var quarantine = Options.QuarantineBucket;

        var purged = await PurgeAsync(file, storage);

        Assert.True(purged);
        Assert.Equal(
            (quarantine, file.ObjectKey, quarantine, $"purged/{file.ObjectKey}"),
            Assert.Single(storage.Copied)
        );
        Assert.Equal((quarantine, file.ObjectKey), Assert.Single(storage.Deleted));
        Assert.Equal([$"copy:{quarantine}->{quarantine}", $"delete:{quarantine}"], storage.Operations);
        Assert.Empty(storage.DeletedAllVersions);
        Assert.Empty(storage.Tagged);
    }

    [Fact]
    public async Task Purge_SoftDeletedInfectedWithoutQuarantineBlob_SkipsCopy()
    {
        var file = NewDocument(4);
        MarkInfected(file);
        var storage = new FakeObjectStorage();

        Assert.True(await PurgeAsync(file, storage));

        Assert.Empty(storage.Copied);
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task Purge_SoftDeletedMultipartNeverAssembled_AbortsUpload()
    {
        var file = NewDocument(4);
        Assert.True(file.AttachMultipartUpload("upload-1").IsSuccess);
        var storage = new FakeObjectStorage();
        var multipart = new FakeMultipartUploadStorage();

        await PurgeAsync(file, storage, multipart: multipart);

        Assert.Equal((Options.TempBucket, file.ObjectKey, "upload-1"), Assert.Single(multipart.Aborted));
        Assert.Empty(storage.Deleted);
    }

    private static FileObject NewDocument(long sizeBytes)
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2026/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "doc.pdf",
                "application/pdf",
                sizeBytes,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
    }

    private static void MarkInfected(FileObject file)
    {
        Assert.True(file.MarkPendingScan(DateTime.UtcNow).IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(file.MarkInfected("Eicar", "application/pdf", DateTime.UtcNow).IsSuccess);
    }

    private static TenantStorageLimit NewLimit(Guid tenantId, long used, long reserved)
    {
        var limit = TenantStorageLimit.Create(tenantId, "starter", 10 * 1024 * Mb, 10 * 1024 * Mb);
        if (used > 0)
        {
            Assert.True(limit.Reserve(used).IsSuccess);
            limit.Commit(used);
        }
        if (reserved > 0)
            Assert.True(limit.Reserve(reserved).IsSuccess);
        return limit;
    }

    private static Task<bool> PurgeAsync(
        FileObject file,
        FakeObjectStorage storage,
        TenantStorageLimit? limit = null,
        FakeMultipartUploadStorage? multipart = null
    )
    {
        Assert.True(file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit ?? TenantStorageLimit.Create(file.TenantId, "starter", 100_000_000, 100_000_000));
        return RecycleBinPurger.PurgeAsync(
            file,
            "test",
            Options,
            files,
            limits,
            new FakeStorageAuditRepository(),
            storage,
            multipart ?? new FakeMultipartUploadStorage(),
            new FakeSystemClock(DateTime.UtcNow),
            null,
            "corr-purge",
            NullLogger.Instance,
            CancellationToken.None
        );
    }
}
