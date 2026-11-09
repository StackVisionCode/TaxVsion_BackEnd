using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class ReserveFundsHandlerTests
{
    [Fact]
    public async Task Handle_EmptyReferenceType_ReturnsFailureAndLeavesHeldUntouched()
    {
        var tenantId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallets.Wallets.Add(wallet);

        var result = await ReserveFundsHandler.Handle(
            new ReserveFundsCommand(tenantId, "", Guid.NewGuid(), new PerChannelUnits(Sms: 1)),
            new FakePriceBookRepository(),
            reservations,
            wallets,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ReservationErrors.ReferenceRequired, result.Error);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Empty(wallets.LedgerEntries);
        Assert.Empty(reservations.Reservations);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_SameReferenceStillOpen_ReturnsExistingWithoutHoldingAgain()
    {
        var (tenantId, wallets, reservations, wallet) = Seed();
        var referenceId = Guid.NewGuid();
        reservations.Reservations.Add(ReservationBuilder.Open(tenantId, "individual-sms", referenceId, 50_000, 1));

        var result = await HandleAsync(tenantId, referenceId, wallets, reservations);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Authorized);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Single(reservations.Reservations);
    }

    [Fact]
    public async Task Handle_SameReferenceAlreadyExpired_IsRejectedSoTheRetryDoesNotSend()
    {
        var (tenantId, wallets, reservations, wallet) = Seed();
        var referenceId = Guid.NewGuid();
        var expired = ReservationBuilder.Open(tenantId, "individual-sms", referenceId, 50_000, 1);
        expired.Expire(DateTime.UtcNow);
        reservations.Reservations.Add(expired);

        var result = await HandleAsync(tenantId, referenceId, wallets, reservations);

        Assert.True(result.IsFailure);
        Assert.Equal(ReservationErrors.Expired, result.Error);
        Assert.Equal(0, wallet.HeldMicros);
    }

    [Fact]
    public async Task Handle_SeveralChannels_HoldsTheExactCost()
    {
        var (tenantId, wallets, reservations, wallet) = Seed();
        var referenceId = Guid.NewGuid();
        var priceBook = new FakePriceBookRepository
        {
            Active = PriceBookVersion
                .Publish(
                    7,
                    new Dictionary<PriceChannel, long> { [PriceChannel.Email] = 1_234, [PriceChannel.Sms] = 10_000 },
                    Guid.NewGuid()
                )
                .Value,
        };

        var result = await ReserveFundsHandler.Handle(
            new ReserveFundsCommand(tenantId, "campaign-run", referenceId, new PerChannelUnits(Email: 3, Sms: 7)),
            priceBook,
            reservations,
            wallets,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.Value.Authorized);
        Assert.Equal(73_702, result.Value.CostMicros);
        Assert.Equal(10_000_000, result.Value.AvailableMicros);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(7, result.Value.PriceBookVersion);
        Assert.Equal(73_702, wallet.HeldMicros);
        Assert.Equal(10_000_000, wallet.PostedMicros);
        var reservation = Assert.Single(reservations.Reservations);
        Assert.Equal(73_702, reservation.ReservedMicros);
        Assert.Equal(10, reservation.ReservedUnits);
        Assert.Equal("USD", reservation.Currency);
        var entry = Assert.Single(wallets.LedgerEntries);
        Assert.Equal(73_702, entry.DeltaHeldMicros);
        Assert.Equal($"wallet-reserve:campaign-run:{referenceId:N}", entry.OperationKey);
    }

    [Fact]
    public async Task Handle_UnpricedChannel_FailsWithoutHolding()
    {
        var (tenantId, wallets, reservations, wallet) = Seed();

        var result = await ReserveFundsHandler.Handle(
            new ReserveFundsCommand(tenantId, "campaign-run", Guid.NewGuid(), new PerChannelUnits(WhatsApp: 1)),
            new FakePriceBookRepository(),
            reservations,
            wallets,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Pricing.ChannelNotPriced", result.Error.Code);
        Assert.Equal("El canal 'WhatsApp' no tiene tarifa en el catálogo vigente.", result.Error.Message);
        Assert.Equal(0, wallet.HeldMicros);
    }

    [Fact]
    public async Task Handle_InsufficientFunds_NotAuthorizedWithTheDeficit()
    {
        var (tenantId, wallets, reservations, wallet) = Seed();

        var result = await ReserveFundsHandler.Handle(
            new ReserveFundsCommand(tenantId, "campaign-run", Guid.NewGuid(), new PerChannelUnits(Sms: 1_001)),
            new FakePriceBookRepository(),
            reservations,
            wallets,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.False(result.Value.Authorized);
        Assert.Equal(10_010_000, result.Value.CostMicros);
        Assert.Equal(10_000, result.Value.DeficitMicros);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Empty(reservations.Reservations);
    }

    private static (Guid, FakeWalletRepository, FakeReservationRepository, WalletAggregate) Seed()
    {
        var tenantId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(Usd(10_000_000), "topup:seed");
        wallets.Wallets.Add(wallet);
        return (tenantId, wallets, new FakeReservationRepository(), wallet);
    }

    private static Task<BuildingBlocks.Results.Result<ReservationView>> HandleAsync(
        Guid tenantId,
        Guid referenceId,
        FakeWalletRepository wallets,
        FakeReservationRepository reservations
    ) =>
        ReserveFundsHandler.Handle(
            new ReserveFundsCommand(tenantId, "individual-sms", referenceId, new PerChannelUnits(Sms: 1)),
            new FakePriceBookRepository(),
            reservations,
            wallets,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
}
