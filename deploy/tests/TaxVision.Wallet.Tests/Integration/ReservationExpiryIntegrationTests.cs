using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Integration;

/// <summary>
/// Contra el SQL Server local: vencer y liquidar la misma reserva a la vez. Gana uno solo (rowversion del
/// monedero o índice único del ledger) y el saldo apartado cuadra con el ledger.
/// </summary>
public sealed class ReservationExpiryIntegrationTests : IAsyncLifetime
{
    private const string ReferenceType = "campaign-run";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _referenceId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        var wallet = WalletAggregate.Create(_tenantId).Value;
        db.LedgerEntries.Add(wallet.Credit(10_000_000, $"topup:{Guid.NewGuid():N}").Value);
        db.LedgerEntries.Add(wallet.Reserve(500_000, $"wallet-reserve:{ReferenceType}:{_referenceId:N}").Value);
        db.Wallets.Add(wallet);
        db.Reservations.Add(ReservationBuilder.Open(_tenantId, ReferenceType, _referenceId, 500_000, 5));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = WalletTestDatabase.NewContext(_tenantId);
        await db.Reservations.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.LedgerEntries.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Wallets.IgnoreQueryFilters().Where(w => w.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ExpireAndSettleInParallel_OnlyOneWins()
    {
        await using var expireDb = WalletTestDatabase.NewContext(_tenantId);
        await using var settleDb = WalletTestDatabase.NewContext(_tenantId);
        // Los dos leen antes de que cualquiera guarde: EF devuelve la entidad ya rastreada.
        await LoadAsync(expireDb);
        await LoadAsync(settleDb);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() =>
                ExpireReservationHandler.Handle(
                    new ExpireReservationCommand(_tenantId, ReferenceType, _referenceId),
                    new ReservationRepository(expireDb),
                    new WalletRepository(expireDb),
                    expireDb,
                    NullLogger<WalletReservation>.Instance,
                    CancellationToken.None
                )
            ),
            CaptureAsync(() =>
                SettleReservationHandler.Handle(
                    new SettleReservationCommand(_tenantId, ReferenceType, _referenceId, ConsumedUnits: 5),
                    new ReservationRepository(settleDb),
                    new WalletRepository(settleDb),
                    settleDb,
                    NullLogger<WalletReservation>.Instance,
                    CancellationToken.None
                )
            )
        );

        var failure = Assert.Single(outcomes, o => o is not null);
        Assert.True(failure is DbUpdateConcurrencyException or ConflictException, failure?.ToString());

        await using var check = WalletTestDatabase.NewContext(_tenantId);
        var wallet = await check.Wallets.IgnoreQueryFilters().SingleAsync(w => w.TenantId == _tenantId);
        var ledgerHeld = await check
            .LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TenantId == _tenantId)
            .SumAsync(e => e.DeltaHeldMicros);
        var reservation = await check.Reservations.IgnoreQueryFilters().SingleAsync(r => r.TenantId == _tenantId);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Equal(0, ledgerHeld);
        Assert.NotEqual(ReservationStatus.Open, reservation.Status);
    }

    private async Task LoadAsync(WalletDbContext db)
    {
        await db.Wallets.IgnoreQueryFilters().SingleAsync(w => w.TenantId == _tenantId);
        await db.Reservations.IgnoreQueryFilters().SingleAsync(r => r.TenantId == _tenantId);
    }

    // Null si el handler terminó sin lanzar; la excepción si chocó al guardar.
    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await Task.Yield();
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
