using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class SettleReservationHandlerTests
{
    [Fact]
    public async Task Handle_SettleFails_DoesNotMutateWallet()
    {
        // Held cubre solo lo consumido: Consume pasaría y Release fallaría después de mutar el monedero.
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(10_000_000, "topup:seed");
        wallet.Reserve(500_000, "reserve:seed");
        wallets.Wallets.Add(wallet);
        reservations.Reservations.Add(ReservationBuilder.Open(tenantId, "campaign-run", referenceId, 1_000_000, 2));

        var result = await SettleReservationHandler.Handle(
            new SettleReservationCommand(tenantId, "campaign-run", referenceId, ConsumedUnits: 1),
            reservations,
            wallets,
            unitOfWork,
            NullLogger<WalletReservation>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(10_000_000, wallet.PostedMicros);
        Assert.Equal(500_000, wallet.HeldMicros);
        Assert.Empty(wallets.LedgerEntries);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_ExpiredReservation_ReturnsExpiredWithoutCharging()
    {
        // Otro hold vivo del tenant: sin la rama de Expired, Settle alcanzaría a cobrar de ahí.
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(10_000_000, "topup:seed");
        wallet.Reserve(500_000, "reserve:other");
        wallet.ClearDomainEvents();
        wallets.Wallets.Add(wallet);
        var reservation = ReservationBuilder.Open(tenantId, "campaign-run", referenceId, 500_000, 5);
        reservation.Expire(DateTime.UtcNow);
        reservations.Reservations.Add(reservation);

        var result = await SettleReservationHandler.Handle(
            new SettleReservationCommand(tenantId, "campaign-run", referenceId, ConsumedUnits: 5),
            reservations,
            wallets,
            unitOfWork,
            NullLogger<WalletReservation>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ReservationErrors.Expired, result.Error);
        Assert.Equal(10_000_000, wallet.PostedMicros);
        Assert.Equal(500_000, wallet.HeldMicros);
        Assert.Empty(wallets.LedgerEntries);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(wallet.DomainEvents);
    }
}
