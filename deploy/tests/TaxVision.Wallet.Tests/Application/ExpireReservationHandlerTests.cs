using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class ExpireReservationHandlerTests
{
    [Fact]
    public async Task Handle_OpenReservation_ReleasesHeldAndPublishesBalanceChanged()
    {
        var tenantId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var wallets = new FakeWalletRepository();
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new TestMessageContext();
        var wallet = WalletAggregate.Create(tenantId).Value;
        wallet.Credit(10_000_000, "topup:seed");
        wallet.Reserve(500_000, "reserve:seed");
        wallets.Wallets.Add(wallet);
        var reservation = ReservationBuilder.Open(tenantId, "campaign-run", referenceId, 500_000, 5);
        reservations.Reservations.Add(reservation);

        var result = await ExpireReservationHandler.Handle(
            new ExpireReservationCommand(tenantId, "campaign-run", referenceId),
            reservations,
            wallets,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<WalletReservation>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Expired);
        Assert.Equal(0, wallet.HeldMicros);
        Assert.Equal(10_000_000, wallet.PostedMicros);
        var entry = Assert.Single(wallets.LedgerEntries);
        Assert.Equal(MovementType.Release, entry.Movement);
        Assert.Equal($"wallet-release:campaign-run:{referenceId:N}", entry.OperationKey);
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
        // TestMessageContext guarda sobres, no mensajes.
        var published = Assert.Single(
            bus.Published.OfType<Envelope>().Select(e => e.Message).OfType<WalletBalanceChangedIntegrationEvent>()
        );
        Assert.Equal("expire", published.Reason);
    }
}
