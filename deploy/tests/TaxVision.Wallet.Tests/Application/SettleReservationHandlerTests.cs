using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet;
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
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallet.Reserve(Usd(500_000), "reserve:seed");
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
    public async Task Handle_UnevenProration_TruncatesConsumedAndReleasesTheRest()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallet.Reserve(Usd(1_000), "reserve:seed");
        wallets.Wallets.Add(wallet);
        reservations.Reservations.Add(ReservationBuilder.Open(tenantId, "campaign-run", referenceId, 1_000, 3));

        var result = await SettleReservationHandler.Handle(
            new SettleReservationCommand(tenantId, "campaign-run", referenceId, ConsumedUnits: 1),
            reservations,
            wallets,
            new FakeUnitOfWork(),
            NullLogger<WalletReservation>.Instance,
            CancellationToken.None
        );

        Assert.Equal(333, result.Value.ConsumedMicros);
        Assert.Equal(667, result.Value.ReleasedMicros);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(10_000_000 - 333, wallet.PostedMicros);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Equal(2, wallets.LedgerEntries.Count);
    }

    [Fact]
    public async Task Handle_ReservationInAnotherCurrency_FailsWithoutMutating()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallet.Reserve(Usd(1_000), "reserve:seed");
        wallets.Wallets.Add(wallet);
        var reservation = WalletReservation
            .Open(
                tenantId,
                ReservationReference.Create("campaign-run", referenceId).Value,
                WalletMoney.Create(1_000, "MXN").Value,
                2,
                1
            )
            .Value;
        reservations.Reservations.Add(reservation);

        var result = await SettleReservationHandler.Handle(
            new SettleReservationCommand(tenantId, "campaign-run", referenceId, ConsumedUnits: 1),
            reservations,
            wallets,
            new FakeUnitOfWork(),
            NullLogger<WalletReservation>.Instance,
            CancellationToken.None
        );

        Assert.Equal(WalletErrors.CurrencyMismatch, result.Error);
        Assert.Equal(ReservationStatus.Open, reservation.Status);
        Assert.Equal(1_000, wallet.HeldMicros);
        Assert.Empty(wallets.LedgerEntries);
    }

    [Fact]
    public async Task Handle_SettledTwice_ChargesOnce()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallet.Reserve(Usd(1_000_000), "reserve:seed");
        wallets.Wallets.Add(wallet);
        reservations.Reservations.Add(ReservationBuilder.Open(tenantId, "individual-sms", referenceId, 1_000_000, 2));
        var command = new SettleReservationCommand(tenantId, "individual-sms", referenceId, ConsumedUnits: 1);

        Task<BuildingBlocks.Results.Result<TaxVision.Wallet.Application.Reservations.SettlementView>> SettleAsync() =>
            SettleReservationHandler.Handle(
                command,
                reservations,
                wallets,
                unitOfWork,
                NullLogger<WalletReservation>.Instance,
                CancellationToken.None
            );

        var first = await SettleAsync();
        var ledgerAfterFirst = wallets.LedgerEntries.Count;
        var second = await SettleAsync();

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(9_500_000, wallet.PostedMicros);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Equal(ledgerAfterFirst, wallets.LedgerEntries.Count);
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
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallet.Reserve(Usd(500_000), "reserve:other");
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
