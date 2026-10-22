using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;

namespace TaxVision.Connectors.Tests.Persistence;

public sealed class TenantEmailAccountRepositoryTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>El ReconciliationJob recorre cuentas de todos los tenants, sin tenant en el contexto.</summary>
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task ListActivePageAsync_WalksEveryActiveAccountOnceAcrossPages()
    {
        var databaseName = Guid.NewGuid().ToString();
        var active = Enumerable.Range(0, 5).Select(_ => CreateAccount(activate: true)).ToList();
        var notActive = CreateAccount(activate: false);

        await using (var seedDb = CreateContext(databaseName))
        {
            await seedDb.TenantEmailAccounts.AddRangeAsync(active);
            await seedDb.TenantEmailAccounts.AddAsync(notActive);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName);
        var repository = new TenantEmailAccountRepository(db);

        var seen = new List<Guid>();
        var pages = 0;
        Guid? afterId = null;
        while (true)
        {
            var page = await repository.ListActivePageAsync(afterId, pageSize: 2);
            if (page.Count == 0)
                break;
            pages++;
            seen.AddRange(page.Select(a => a.Id));
            afterId = page[^1].Id;
        }

        Assert.Equal(3, pages);
        Assert.Equal(active.Select(a => a.Id).Order(), seen.Order());
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    private static ConnectorsDbContext CreateContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<ConnectorsDbContext>().UseInMemoryDatabase(databaseName).Options,
            new NoTenantContext()
        );

    private static TenantEmailAccount CreateAccount(bool activate)
    {
        var account = TenantEmailAccount
            .Create(Guid.NewGuid(), $"user-{Guid.NewGuid():N}@example.com", ProviderCode.Imap, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        if (activate)
            account.Activate(Now);
        return account;
    }
}
