using TaxVision.Wallet.Application.Reservations;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations.Commands;
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
        wallet.Credit(10_000_000, "topup:seed");
        wallets.Wallets.Add(wallet);

        var result = await ReserveFundsHandler.Handle(
            new ReserveFundsCommand(tenantId, "", Guid.NewGuid(), new PerChannelUnits(Sms: 1)),
            new FakePriceBookRepository(),
            reservations,
            wallets,
            unitOfWork,
            new TestMessageContext(),
            new FakeCorrelationContext(),
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

    private static (Guid, FakeWalletRepository, FakeReservationRepository, WalletAggregate) Seed()
    {
        var tenantId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(10_000_000, "topup:seed");
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
            new TestMessageContext(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );
}
