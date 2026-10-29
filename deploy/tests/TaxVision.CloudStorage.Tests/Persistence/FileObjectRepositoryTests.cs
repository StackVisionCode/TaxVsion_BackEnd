using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>Fotos de perfil F0 — el listado plano (GET storage/files) nunca muestra Avatars salvo que se pida explícito.</summary>
public sealed class FileObjectRepositoryTests
{
    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task ListAsync_hides_avatars_by_default_and_keeps_paging_on_documents_only()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var start = DateTime.UtcNow;
        // El avatar es el más reciente: si se filtrara en memoria tras paginar, la página 1 saldría corta.
        var document = AvatarTestFiles.Document(tenantId, customerId, start);
        var avatar = AvatarTestFiles.Avatar(
            tenantId,
            OwnerType.Customer,
            customerId,
            createdAtUtc: start.AddMinutes(1)
        );
        await using var db = await SeedAsync(tenantId, document, avatar);

        var page = await new FileObjectRepository(db).ListAsync(
            tenantId,
            null,
            null,
            null,
            null,
            null,
            0,
            1,
            CancellationToken.None
        );

        Assert.Equal(document.Id, Assert.Single(page).Id);
    }

    [Fact]
    public async Task ListAsync_hides_avatars_from_the_portal_scope_too()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var avatar = AvatarTestFiles.Avatar(tenantId, OwnerType.Customer, customerId);
        await using var db = await SeedAsync(tenantId, avatar);

        var portal = await new FileObjectRepository(db).ListAsync(
            tenantId,
            customerId,
            null,
            null,
            null,
            null,
            0,
            50,
            CancellationToken.None
        );

        Assert.Empty(portal);
    }

    [Fact]
    public async Task ListAsync_returns_only_avatars_when_explicitly_filtered()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var document = AvatarTestFiles.Document(tenantId, customerId);
        var avatar = AvatarTestFiles.Avatar(tenantId, OwnerType.User, Guid.NewGuid());
        await using var db = await SeedAsync(tenantId, document, avatar);

        var avatars = await new FileObjectRepository(db).ListAsync(
            tenantId,
            null,
            null,
            null,
            null,
            FolderType.Avatars,
            0,
            50,
            CancellationToken.None
        );

        Assert.Equal(avatar.Id, Assert.Single(avatars).Id);
    }

    [Fact]
    public async Task ListSoftDeletedLooseAsync_hides_deleted_profile_photos_from_the_recycle_bin()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var document = AvatarTestFiles.Document(tenantId, Guid.NewGuid(), now);
        var avatar = AvatarTestFiles.Avatar(tenantId, OwnerType.User, Guid.NewGuid(), createdAtUtc: now);
        Assert.True(document.SoftDelete(now, TimeSpan.FromDays(30)).IsSuccess);
        Assert.True(avatar.SoftDelete(now.AddMinutes(1), TimeSpan.FromDays(30)).IsSuccess);
        await using var db = await SeedAsync(tenantId, document, avatar);
        var repository = new FileObjectRepository(db);

        var bin = await repository.ListSoftDeletedLooseAsync(tenantId, 0, 50, CancellationToken.None);
        // La purga (ListSoftDeletedAsync) sí los sigue viendo: si no, los bytes no se borrarían nunca.
        var purgeable = await repository.ListSoftDeletedAsync(tenantId, 0, 50, CancellationToken.None);

        Assert.Equal(document.Id, Assert.Single(bin).Id);
        Assert.Contains(purgeable, f => f.Id == avatar.Id);
    }

    private static async Task<CloudStorageDbContext> SeedAsync(Guid tenantId, params FileObject[] files)
    {
        var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext(tenantId)
        );
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
