using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>"Security activity" de una ficha: filtra por objetivo sin cruzar de oficina.</summary>
public sealed class AuthAuditStoreTests
{
    [Fact]
    public async Task The_target_filter_returns_only_that_target_in_the_office()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await using (var seed = CreateContext(databaseName))
        {
            seed.AuthAuditLogs.AddRange(
                Log(tenantId, targetId),
                Log(tenantId, Guid.NewGuid()),
                Log(tenantId, null),
                Log(Guid.NewGuid(), targetId)
            );
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var (items, total) = await new AuthAuditStore(db).GetPagedAsync(
            tenantId,
            null,
            null,
            null,
            null,
            1,
            50,
            targetId: targetId
        );

        Assert.Equal(1, total);
        var only = Assert.Single(items);
        Assert.Equal((tenantId, targetId), (only.TenantId, only.TargetId));
    }

    private static AuthAuditLog Log(Guid tenantId, Guid? targetId) =>
        AuthAuditLog.Record(
            tenantId,
            Guid.NewGuid(),
            "auth.test",
            true,
            null,
            null,
            null,
            "User",
            targetId,
            actorType: "TenantAdmin"
        );

    private static AuthDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options, new NoTenant());

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
