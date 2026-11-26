using BuildingBlocks.Messaging.WalletIntegrationEvents;
using TaxVision.Wallet.Application.Settings.DomainEvents;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Domain.Wallet.Events;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

public sealed class WalletBalanceThresholdsHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _consentUserId = Guid.NewGuid();
    private readonly FakeWalletRepository _wallets = new();
    private readonly FakeWalletSettingsRepository _settings = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeTenantEntitlementModulesReader _modules = new();

    [Fact]
    public async Task Handle_BalanceAboveThresholdWithoutSettings_DoesNothing()
    {
        SeedWallet(10_000_000);

        var messages = await HandleAsync();

        Assert.Empty(messages);
        Assert.Empty(_settings.Settings);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_CrossesTheDefaultThreshold_SavesThenPublishesLowBalanceOnce()
    {
        SeedWallet(4_000_000);

        var first = await HandleAsync();
        var second = await HandleAsync();

        var alert = Assert.IsType<WalletLowBalanceIntegrationEvent>(Assert.Single(first));
        Assert.Equal(_tenantId, alert.TenantId);
        Assert.Equal(4_000_000, alert.AvailableMicros);
        Assert.Equal(5_000_000, alert.ThresholdMicros);
        Assert.Equal("USD", alert.Currency);
        Assert.Equal("corr-1", alert.CorrelationId);
        Assert.Empty(second);
        Assert.Single(_settings.Settings);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_AutoRechargeActive_CreatesAutomaticTopUpAndPublishesTopUpDue()
    {
        SeedWallet(10_000_000);
        var settings = SeedActiveAutoRecharge(thresholdMicros: 20_000_000);

        var messages = await HandleAsync();

        var topUp = Assert.Single(_wallets.TopUps);
        Assert.True(topUp.Automatic);
        Assert.Equal(2_500, topUp.AmountCents);
        Assert.Equal(_consentUserId, topUp.RequestedByUserId);
        var due = Assert.Single(messages.OfType<WalletTopUpDueIntegrationEvent>());
        Assert.Equal(topUp.Id, due.TopUpId);
        Assert.True(due.Automatic);
        Assert.Equal("Stripe", due.Provider);
        Assert.Equal("pm_1", due.PaymentMethodReference);
        Assert.Equal(_consentUserId, due.RequestedByUserId);
        Assert.Equal(topUp.IdempotencyKey, due.IdempotencyKey);
        Assert.Equal(1, settings.RechargesOn(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_AutomaticAlreadyPending_DoesNotCreateAnother()
    {
        SeedWallet(10_000_000);
        SeedActiveAutoRecharge(thresholdMicros: 20_000_000);

        await HandleAsync();
        var second = await HandleAsync();

        Assert.Single(_wallets.TopUps);
        Assert.Empty(second.OfType<WalletTopUpDueIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_DailyLimitReached_PublishesTheNoticeOnce()
    {
        SeedWallet(10_000_000);
        SeedActiveAutoRecharge(thresholdMicros: 20_000_000);

        var published = new List<object>();
        for (var i = 0; i < 5; i++)
        {
            published.AddRange(await HandleAsync());
            foreach (var pending in _wallets.TopUps.Where(t => t.Status == WalletTopUpStatus.Pending))
                pending.MarkCredited(Guid.NewGuid());
        }

        Assert.Equal(3, _wallets.TopUps.Count);
        var notice = Assert.Single(published.OfType<WalletAutoRechargeDailyLimitReachedIntegrationEvent>());
        Assert.Equal(3, notice.DailyLimit);
        Assert.Equal(2_500, notice.AmountCents);
    }

    // Oficina sin el módulo: sin recarga automática (no se cobra la tarjeta) y sin consumir el cruce del umbral.
    [Fact]
    public async Task Handle_WithoutTheModule_DoesNotRechargeAndRechargesOnceReactivated()
    {
        SeedWallet(10_000_000);
        var settings = SeedActiveAutoRecharge(thresholdMicros: 20_000_000);
        _modules.Modules = ["clients"];

        var suspended = await HandleAsync();

        Assert.Empty(suspended);
        Assert.Empty(_wallets.TopUps);
        Assert.Equal(0, settings.RechargesOn(DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.Equal(0, _unitOfWork.SaveCount);

        _modules.Modules = ["wallet"];
        var reactivated = await HandleAsync();

        Assert.Single(_wallets.TopUps);
        Assert.Single(reactivated.OfType<WalletTopUpDueIntegrationEvent>());
    }

    // Oficina sin el módulo: sin aviso de saldo bajo; sale en el primer cambio de saldo tras reactivar.
    [Fact]
    public async Task Handle_WithoutTheModule_DoesNotPublishTheLowBalanceNotice()
    {
        SeedWallet(4_000_000);
        _modules.Modules = [];

        var suspended = await HandleAsync();

        Assert.Empty(suspended);
        Assert.Empty(_settings.Settings);
        Assert.Equal(0, _unitOfWork.SaveCount);

        _modules.Modules = null;
        Assert.IsType<WalletLowBalanceIntegrationEvent>(Assert.Single(await HandleAsync()));
    }

    private void SeedWallet(long availableMicros)
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        wallet.Credit(Usd(availableMicros), "topup:seed");
        wallet.ClearDomainEvents();
        _wallets.Wallets.Add(wallet);
    }

    private WalletSettings SeedActiveAutoRecharge(long thresholdMicros)
    {
        var settings = WalletSettings.CreateDefault(_tenantId);
        settings.ChangeLowBalanceThreshold(0, _consentUserId, DateTime.UtcNow);
        settings.EnableAutoRecharge(
            2_500,
            thresholdMicros,
            AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value,
            new ConsentGrant(_consentUserId, DateTime.UtcNow, "203.0.113.9", AutoRechargeConsentTemplate.Version, "ok")
        );
        _settings.Settings.Add(settings);
        return settings;
    }

    private async Task<IReadOnlyList<object>> HandleAsync() =>
        (
            await WalletBalanceThresholdsHandler.Handle(
                new WalletBalanceChangedDomainEvent(Guid.NewGuid(), _tenantId, MovementType.Consume, DateTime.UtcNow),
                _wallets,
                _settings,
                new FakeOfficeTimeZone(),
                _modules,
                _unitOfWork,
                new Envelope { CorrelationId = "corr-1" },
                CancellationToken.None
            )
        ).ToList();
}
