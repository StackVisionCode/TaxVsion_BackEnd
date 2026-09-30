using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>El reset por enlace corre sin tenant en contexto: la búsqueda de enlaces pendientes debe saltarse el
/// filtro de tenant y quedarse solo con los de esa cuenta que todavía sirven.</summary>
public sealed class CredentialTokenRepositoryTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) => throw new NotSupportedException();
    }

    [Fact]
    public async Task GetPendingPasswordResetsAsync_returns_only_the_account_links_that_still_work()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var pending = NewLink(tenantId, userId, TimeSpan.FromMinutes(30));
        var used = NewLink(tenantId, userId, TimeSpan.FromMinutes(30));
        used.MarkUsed();
        var revoked = NewLink(tenantId, userId, TimeSpan.FromMinutes(30));
        revoked.Revoke(now);
        var expired = NewLink(tenantId, userId, TimeSpan.FromMinutes(-1));
        var otherAccount = NewLink(tenantId, Guid.NewGuid(), TimeSpan.FromMinutes(30));

        await using (var seedDb = CreateContext(databaseName))
        {
            await seedDb.PasswordResetTokens.AddRangeAsync(pending, used, revoked, expired, otherAccount);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var found = await new CredentialTokenRepository(db).GetPendingPasswordResetsAsync(userId, now);

        Assert.Equal(pending.Id, Assert.Single(found).Id);
    }

    private static PasswordResetToken NewLink(Guid tenantId, Guid userId, TimeSpan validity) =>
        PasswordResetToken.Create(tenantId, userId, Guid.NewGuid().ToString("N").PadRight(64, '0'), null, validity);

    private static AuthDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            new NoTenantContext()
        );
}
