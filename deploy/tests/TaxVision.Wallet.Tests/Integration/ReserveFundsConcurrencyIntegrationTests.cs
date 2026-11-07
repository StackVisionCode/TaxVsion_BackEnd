using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations;
using TaxVision.Wallet.Application.Reservations.Commands;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>WAL-6: reservas simultáneas del mismo tenant por el camino del controller. El perdedor reintenta
/// en un contexto nuevo y entra; ninguna termina en error.</summary>
public sealed class ReserveFundsConcurrencyIntegrationTests : IAsyncLifetime
{
    private const long OneDollarMicros = 1_000_000;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _schema = WalletIntegrationHost.NewSchema();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await using (var db = WalletTestDatabase.NewContext(_tenantId))
        {
            var wallet = WalletAggregate.Create(_tenantId).Value;
            db.LedgerEntries.Add(wallet.Credit(10 * OneDollarMicros, $"topup:{Guid.NewGuid():N}").Value);
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();
        }

        _host = await WalletIntegrationHost.StartAsync(_schema, _tenantId, [typeof(ReserveFundsHandler)]);
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        await using (var db = WalletTestDatabase.NewContext(_tenantId))
        {
            await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Reservations.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Wallets.IgnoreQueryFilters().Where(w => w.TenantId == _tenantId).ExecuteDeleteAsync();
        }

        await WalletIntegrationHost.DropSchemaAsync(_schema);
    }

    [Fact]
    public async Task TwoParallelReserves_DifferentReferences_BothAuthorized()
    {
        var results = await Task.WhenAll(ReserveAsync(Guid.NewGuid()), ReserveAsync(Guid.NewGuid()));

        Assert.All(results, result => Assert.True(result.Value.Authorized));

        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var wallet = await db.Wallets.AsNoTracking().SingleAsync(w => w.TenantId == _tenantId);
        Assert.Equal(2 * OneDollarMicros, wallet.HeldMicros);
        Assert.Equal(2, await db.Reservations.CountAsync(r => r.TenantId == _tenantId));
        Assert.Equal(2, await db.LedgerEntries.CountAsync(e => e.TenantId == _tenantId && e.DeltaHeldMicros > 0));
    }

    // 100 SMS a 10_000 micros = 1 USD.
    private Task<Result<ReservationView>> ReserveAsync(Guid referenceId)
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        var command = new ReserveFundsCommand(_tenantId, "campaign-run", referenceId, new PerChannelUnits(Sms: 100));
        return ReservationConflictRetry.InvokeAsync(
            token => bus.InvokeAsync<Result<ReservationView>>(command, token),
            maxAttempts: 3,
            CancellationToken.None
        );
    }
}
