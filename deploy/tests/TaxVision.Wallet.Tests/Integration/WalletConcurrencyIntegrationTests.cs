using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>WAL-6: dos contextos que reservan sobre el mismo monedero. El perdedor recibe un conflicto
/// traducido (409 por HTTP), no la excepción cruda de EF (500).</summary>
public sealed class WalletConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var wallet = WalletAggregate.Create(_tenantId).Value;
        db.LedgerEntries.Add(wallet.Credit(Usd(10_000_000), $"topup:{Guid.NewGuid():N}").Value);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Wallets.IgnoreQueryFilters().Where(w => w.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoContextsReserveSameWallet_SecondThrowsConflictException()
    {
        await using var first = WalletTestDatabase.NewContext(_tenantId);
        await using var second = WalletTestDatabase.NewContext(_tenantId);
        var firstWallet = await first.Wallets.SingleAsync(w => w.TenantId == _tenantId);
        var secondWallet = await second.Wallets.SingleAsync(w => w.TenantId == _tenantId);

        first.LedgerEntries.Add(firstWallet.Reserve(Usd(1_000_000), $"a:{Guid.NewGuid():N}", Guid.NewGuid()).Value);
        second.LedgerEntries.Add(secondWallet.Reserve(Usd(1_000_000), $"b:{Guid.NewGuid():N}", Guid.NewGuid()).Value);

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }
}
