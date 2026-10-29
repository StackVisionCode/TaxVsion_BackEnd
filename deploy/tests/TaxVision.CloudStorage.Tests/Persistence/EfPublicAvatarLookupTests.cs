using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>
/// Fotos de perfil F0 — lookup del endpoint anónimo: cross-tenant (sin tenant en el contexto, como una
/// petición sin JWT) y solo Avatars disponibles y sin borrar.
/// </summary>
public sealed class EfPublicAvatarLookupTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("No tenant in an anonymous request.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static async Task<CloudStorageDbContext> SeedAsync(params FileObject[] files)
    {
        var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Theory]
    [InlineData(OwnerType.User)]
    [InlineData(OwnerType.Customer)]
    public async Task Available_avatar_of_any_tenant_is_found_without_a_tenant_context(OwnerType ownerType)
    {
        var avatar = AvatarTestFiles.Avatar(Guid.NewGuid(), ownerType, Guid.NewGuid());
        await using var db = await SeedAsync(avatar);

        var found = await new EfPublicAvatarLookup(db).FindServableAsync(avatar.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(avatar.ObjectKey, found!.ObjectKey);
        Assert.Equal("image/webp", found.ContentType);
        Assert.Equal(AvatarTestFiles.Sha256, found.Sha256);
    }

    [Fact]
    public async Task A_document_is_not_served_even_with_its_real_fileId()
    {
        var document = AvatarTestFiles.Document(Guid.NewGuid(), Guid.NewGuid());
        await using var db = await SeedAsync(document);

        Assert.Null(await new EfPublicAvatarLookup(db).FindServableAsync(document.Id, CancellationToken.None));
    }

    [Fact]
    public async Task An_avatar_still_being_scanned_is_not_served()
    {
        var pending = AvatarTestFiles.Avatar(Guid.NewGuid(), OwnerType.User, Guid.NewGuid(), available: false);
        await using var db = await SeedAsync(pending);

        Assert.Null(await new EfPublicAvatarLookup(db).FindServableAsync(pending.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_soft_deleted_avatar_is_not_served()
    {
        var deleted = AvatarTestFiles.Avatar(Guid.NewGuid(), OwnerType.User, Guid.NewGuid());
        Assert.True(deleted.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        await using var db = await SeedAsync(deleted);

        Assert.Null(await new EfPublicAvatarLookup(db).FindServableAsync(deleted.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_fileId_is_null()
    {
        await using var db = await SeedAsync();

        Assert.Null(await new EfPublicAvatarLookup(db).FindServableAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
