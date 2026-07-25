using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Suppression;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

namespace TaxVision.Postmaster.Tests.Suppression;

public sealed class SuppressionListRepositoryTests
{
    // SuppressionListEntry no implementa ITenantOwned (ver PostmasterDbContext) — el filtro
    // global de RBAC Fase 5 no lo alcanza, así que un tenant vacío acá es inofensivo.
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static PostmasterDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    [Fact]
    public async Task GetSuppressedAsync_returns_only_addresses_present_for_the_tenant()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var repository = new SuppressionListRepository(db);

        await repository.AddAsync(
            SuppressionListEntry
                .Create(tenantId, "bounced@example.com", SuppressionReason.HardBounce, null, null, DateTime.UtcNow)
                .Value
        );
        await repository.AddAsync(
            SuppressionListEntry
                .Create(
                    otherTenantId,
                    "other-tenant@example.com",
                    SuppressionReason.Manual,
                    null,
                    null,
                    DateTime.UtcNow
                )
                .Value
        );
        await db.SaveChangesAsync();

        var result = await repository.GetSuppressedAsync(
            tenantId,
            ["bounced@example.com", "clean@example.com", "other-tenant@example.com"]
        );

        Assert.Single(result);
        Assert.Contains("bounced@example.com", result);
    }

    [Fact]
    public async Task GetSuppressedAsync_returns_empty_set_for_empty_input()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);

        var result = await repository.GetSuppressedAsync(Guid.NewGuid(), []);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RemoveAsync_deletes_existing_entry_and_returns_true()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var repository = new SuppressionListRepository(db);
        await repository.AddAsync(
            SuppressionListEntry
                .Create(tenantId, "user@example.com", SuppressionReason.Manual, null, null, DateTime.UtcNow)
                .Value
        );
        await db.SaveChangesAsync();

        var removed = await repository.RemoveAsync(tenantId, "USER@example.com");
        await db.SaveChangesAsync();

        Assert.True(removed);
        Assert.False(await db.SuppressionListEntries.AnyAsync());
    }

    [Fact]
    public async Task RemoveAsync_returns_false_when_entry_does_not_exist()
    {
        await using var db = CreateContext();
        var repository = new SuppressionListRepository(db);

        var removed = await repository.RemoveAsync(Guid.NewGuid(), "missing@example.com");

        Assert.False(removed);
    }

    [Fact]
    public async Task ListAsync_filters_by_reason_and_paginates()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var repository = new SuppressionListRepository(db);
        var now = DateTime.UtcNow;
        await repository.AddAsync(
            SuppressionListEntry.Create(tenantId, "a@example.com", SuppressionReason.HardBounce, null, null, now).Value
        );
        await repository.AddAsync(
            SuppressionListEntry.Create(tenantId, "b@example.com", SuppressionReason.Manual, null, null, now).Value
        );
        await db.SaveChangesAsync();

        var result = await repository.ListAsync(
            tenantId,
            addressFilter: null,
            reasonFilter: SuppressionReason.HardBounce,
            page: 1,
            pageSize: 50
        );

        var entry = Assert.Single(result);
        Assert.Equal("a@example.com", entry.EmailAddress);
    }
}
