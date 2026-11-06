using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Wallet.Consumers;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class WalletTopUpResultConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeWalletRepository _wallets = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private WalletTopUpPaymentSucceededIntegrationEvent Succeeded(Guid topUpId, long amountCents, string currency) =>
        new()
        {
            TenantId = _tenantId,
            TopUpId = topUpId,
            SaaSPaymentId = Guid.NewGuid(),
            IdempotencyKey = $"wallet-topup:{topUpId:N}",
            ExternalPaymentReference = "pi_test",
            AmountCents = amountCents,
            Currency = currency,
            PaidAtUtc = DateTime.UtcNow,
            RequestedByUserId = Guid.NewGuid(),
        };

    private Task HandleAsync(WalletTopUpPaymentSucceededIntegrationEvent evt) =>
        WalletTopUpResultConsumer.Handle(
            evt,
            _wallets,
            _unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<WalletTopUp>.Instance,
            CancellationToken.None
        );

    private WalletAggregate SeedWallet()
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        _wallets.Wallets.Add(wallet);
        return wallet;
    }

    private void AssertNotCredited(WalletAggregate wallet, WalletTopUp topUp)
    {
        Assert.Equal(0, wallet.PostedMicros);
        Assert.Empty(_wallets.LedgerEntries);
        Assert.Empty(_wallets.FundingCredits);
        Assert.Equal(WalletTopUpStatus.NeedsReview, topUp.Status);
        Assert.Equal(1, _unitOfWork.SaveCount);
        // Sin acreditar no cambia el saldo: ningún aviso pendiente.
        Assert.Empty(wallet.DomainEvents);
    }

    [Fact]
    public async Task Handle_SucceededEventCurrencyDiffersFromOrder_MarksNeedsReviewAndDoesNotCredit()
    {
        var wallet = SeedWallet();
        var topUp = WalletTopUpBuilder.Pending(_tenantId, 10_000);
        _wallets.TopUps.Add(topUp);

        await HandleAsync(Succeeded(topUp.Id, 10_000, "MXN"));

        AssertNotCredited(wallet, topUp);
    }

    [Fact]
    public async Task Handle_PendingLegacyMxnOrderPaid_MarksNeedsReview()
    {
        var wallet = SeedWallet();
        var topUp = WalletTopUpBuilder.PendingLegacy(_tenantId, 10_000, "MXN");
        _wallets.TopUps.Add(topUp);

        await HandleAsync(Succeeded(topUp.Id, 10_000, "MXN"));

        AssertNotCredited(wallet, topUp);
    }

    [Fact]
    public async Task Handle_AmountDiffersFromOrder_MarksNeedsReview()
    {
        var wallet = SeedWallet();
        var topUp = WalletTopUpBuilder.Pending(_tenantId, 10_000);
        _wallets.TopUps.Add(topUp);

        await HandleAsync(Succeeded(topUp.Id, 1_000_000, "USD"));

        AssertNotCredited(wallet, topUp);
    }

    [Fact]
    public async Task Handle_SucceededForUnknownTopUp_ThrowsWalletTopUpNotFound()
    {
        SeedWallet();

        await Assert.ThrowsAsync<WalletTopUpNotFoundException>(() =>
            HandleAsync(Succeeded(Guid.NewGuid(), 10_000, "USD"))
        );
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_CreditRejected_MarksNeedsReview()
    {
        var wallet = SeedWallet();
        var topUp = WalletTopUpBuilder.PendingLegacy(_tenantId, 0, "USD");
        _wallets.TopUps.Add(topUp);

        await HandleAsync(Succeeded(topUp.Id, 0, "USD"));

        AssertNotCredited(wallet, topUp);
    }

    [Fact]
    public async Task Handle_NoWalletYet_CreatesUsdWalletEvenIfEventSaysOtherwise()
    {
        var topUp = WalletTopUpBuilder.Pending(_tenantId, 10_000);
        _wallets.TopUps.Add(topUp);

        await HandleAsync(Succeeded(topUp.Id, 10_000, "usd"));

        var wallet = Assert.Single(_wallets.Wallets);
        Assert.Equal(WalletAggregate.DefaultCurrency, wallet.Currency);
        Assert.Equal(10_000 * 10_000, wallet.PostedMicros);
        Assert.Equal(WalletTopUpStatus.Credited, topUp.Status);
    }
}
