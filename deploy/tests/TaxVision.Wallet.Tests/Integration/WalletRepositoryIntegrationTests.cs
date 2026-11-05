using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>Contra el SQL Server local: el monedero se busca por (TenantId, Currency), no solo por tenant.</summary>
public sealed class WalletRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        db.Wallets.Add(WalletAggregate.Create(_tenantId, "MXN").Value);
        db.Wallets.Add(WalletAggregate.Create(_tenantId, "USD").Value);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        await db.Wallets.IgnoreQueryFilters().Where(w => w.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetByTenantAsync_TwoWalletsSameTenant_ReturnsRequestedCurrency()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var repository = new WalletRepository(db);

        var usd = await repository.GetByTenantAsync(_tenantId, "USD");
        var mxn = await repository.GetByTenantAsync(_tenantId, "MXN");

        Assert.Equal("USD", usd?.Currency);
        Assert.Equal("MXN", mxn?.Currency);
    }
}
