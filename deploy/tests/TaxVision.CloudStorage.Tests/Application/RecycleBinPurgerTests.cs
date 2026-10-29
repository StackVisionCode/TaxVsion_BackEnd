using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fotos de perfil F0 — la purga de un avatar borra todas sus versiones; los documentos siguen igual.</summary>
public sealed class RecycleBinPurgerTests
{
    private const string MainBucket = "taxvision-storage";

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

    private static Task<bool> PurgeAsync(FileObject file, FakeObjectStorage storage)
    {
        Assert.True(file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(file.TenantId, "starter", 100_000_000, 100_000_000));
        return RecycleBinPurger.PurgeAsync(
            file,
            "test",
            MainBucket,
            files,
            limits,
            new FakeStorageAuditRepository(),
            storage,
            new FakeSystemClock(DateTime.UtcNow),
            null,
            "corr-purge",
            NullLogger.Instance,
            CancellationToken.None
        );
    }
}
