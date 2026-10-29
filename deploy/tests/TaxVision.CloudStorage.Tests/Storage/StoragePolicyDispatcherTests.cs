using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Storage;

/// <summary>Fotos de perfil F0 — una foto de perfil nunca se espeja al Drive/OneDrive/Dropbox de la oficina.</summary>
public sealed class StoragePolicyDispatcherTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException();
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class CountingAdapter : IStoragePolicyProviderAdapter
    {
        public string ProviderId => "google-drive";
        public List<Guid> Copied { get; } = [];

        public Task CopyAvailableFileAsync(FileAvailableIntegrationEvent message, CancellationToken ct)
        {
            Copied.Add(message.FileId);
            return Task.CompletedTask;
        }

        public Task BackupTenantAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("Avatars", false)]
    [InlineData("Documents", true)]
    public async Task Only_non_avatar_files_are_mirrored_in_primary_mode(string folderType, bool mirrored)
    {
        var tenantId = Guid.NewGuid();
        await using var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var policy = TenantStoragePolicy.Create(tenantId, "google-drive");
        policy.Configure("google-drive", "primary", null, null, null);
        db.StoragePolicies.Add(policy);
        await db.SaveChangesAsync();
        var adapter = new CountingAdapter();

        await new StoragePolicyDispatcher(db, [adapter]).CopyAvailableFileAsync(
            new FileAvailableIntegrationEvent
            {
                TenantId = tenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = "tenants/t/user/u/x.webp",
                ContentType = "image/webp",
                SizeBytes = 10,
                ChecksumSha256 = new string('a', 64),
                CreatedBy = Guid.NewGuid(),
                OwnerType = "User",
                OwnerId = Guid.NewGuid(),
                FolderType = folderType,
                OriginalName = "x.webp",
            },
            CancellationToken.None
        );

        Assert.Equal(mirrored ? 1 : 0, adapter.Copied.Count);
    }
}
