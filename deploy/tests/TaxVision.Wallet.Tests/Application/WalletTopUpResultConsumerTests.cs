using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Wallet.Consumers;
using TaxVision.Wallet.Domain.Settings;
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

    private readonly FakeWalletSettingsRepository _settings = new();

    private async Task<IReadOnlyList<object>> HandleAsync(WalletTopUpPaymentSucceededIntegrationEvent evt) =>
        (
            await WalletTopUpResultConsumer.Handle(
                evt,
                _wallets,
                _unitOfWork,
                new FakeCorrelationContext(),
                NullLogger<WalletTopUp>.Instance,
                CancellationToken.None
            )
        ).ToList();

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

    [Fact]
    public async Task Handle_ManualTopUpCredited_KeepsTheReferenceTypeAndPublishesNoReceipt()
    {
        SeedWallet();
        var topUp = WalletTopUpBuilder.Pending(_tenantId, 10_000);
        _wallets.TopUps.Add(topUp);

        var messages = await HandleAsync(Succeeded(topUp.Id, 10_000, "USD"));

        Assert.Equal("topup", Assert.Single(_wallets.LedgerEntries).ReferenceType);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task Handle_AutomaticTopUpCredited_PublishesTheReceipt()
    {
        var wallet = SeedWallet();
        var topUp = Automatic();

        var messages = await HandleAsync(Succeeded(topUp.Id, 2_500, "USD"));

        var receipt = Assert.IsType<WalletAutoRechargeSucceededIntegrationEvent>(Assert.Single(messages));
        Assert.Equal(topUp.Id, receipt.TopUpId);
        Assert.Equal(2_500, receipt.AmountCents);
        Assert.Equal("Visa 4242", receipt.MethodLabel);
        Assert.Equal(wallet.AvailableMicros, receipt.AvailableAfterMicros);
        Assert.Equal(_tenantId, receipt.TenantId);
    }

    [Theory]
    [InlineData("MethodUnavailable", AutoRechargePauseReason.MethodUnavailable)]
    [InlineData("card_declined", AutoRechargePauseReason.PaymentFailed)]
    public async Task Handle_AutomaticTopUpFailed_PausesAndPublishesTheNotice(
        string reason,
        AutoRechargePauseReason expected
    )
    {
        var settings = ActiveSettings();
        var topUp = Automatic();

        var messages = await HandleFailedAsync(Failed(topUp.Id, reason));

        Assert.Equal(WalletTopUpStatus.Failed, topUp.Status);
        Assert.Equal(AutoRechargeStatus.Paused, settings.AutoRechargeStatus);
        Assert.Equal(expected, settings.AutoRechargePausedReason);
        Assert.Equal(WalletSettingsAction.AutoRechargePaused, Assert.Single(_settings.Changes).Action);
        var notice = Assert.IsType<WalletAutoRechargePausedIntegrationEvent>(Assert.Single(messages));
        Assert.Equal(expected.ToString(), notice.Reason);
        Assert.Equal(topUp.Id, notice.TopUpId);
        Assert.Equal("Visa 4242", notice.MethodLabel);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_ManualTopUpFailed_DoesNotTouchAutoRecharge()
    {
        var settings = ActiveSettings();
        var topUp = WalletTopUpBuilder.Pending(_tenantId, 10_000);
        _wallets.TopUps.Add(topUp);

        var messages = await HandleFailedAsync(Failed(topUp.Id, "card_declined"));

        Assert.Equal(WalletTopUpStatus.Failed, topUp.Status);
        Assert.Equal(AutoRechargeStatus.Active, settings.AutoRechargeStatus);
        Assert.Empty(messages);
    }

    private WalletTopUp Automatic()
    {
        var topUp = WalletTopUp.CreateAutomatic(_tenantId, 2_500, "USD", Guid.NewGuid(), Method()).Value;
        _wallets.TopUps.Add(topUp);
        return topUp;
    }

    private WalletSettings ActiveSettings()
    {
        var settings = WalletSettings.CreateDefault(_tenantId);
        settings.EnableAutoRecharge(
            2_500,
            5_000_000,
            Method(),
            new ConsentGrant(Guid.NewGuid(), DateTime.UtcNow, null, AutoRechargeConsentTemplate.Version, "ok")
        );
        _settings.Settings.Add(settings);
        return settings;
    }

    private static AutoRechargeMethod Method() => AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value;

    private WalletTopUpPaymentFailedIntegrationEvent Failed(Guid topUpId, string reason) =>
        new()
        {
            TenantId = _tenantId,
            TopUpId = topUpId,
            IdempotencyKey = $"wallet-topup:{topUpId:N}",
            Reason = reason,
            RequestedByUserId = Guid.NewGuid(),
        };

    private async Task<IReadOnlyList<object>> HandleFailedAsync(WalletTopUpPaymentFailedIntegrationEvent evt) =>
        (
            await WalletTopUpResultConsumer.Handle(
                evt,
                _wallets,
                _settings,
                _unitOfWork,
                new FakeCorrelationContext(),
                NullLogger<WalletTopUp>.Instance,
                CancellationToken.None
            )
        ).ToList();
}
