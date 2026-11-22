using TaxVision.Wallet.Application.Wallet;
using TaxVision.Wallet.Application.Wallet.Queries;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Tests.TestDoubles;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>GET /wallet trae las señales de saldo del navbar: umbral, saldo bajo y estado de la recarga automática.</summary>
public sealed class GetWalletHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeWalletRepository _wallets = new();
    private readonly FakeWalletSettingsRepository _settings = new();

    [Fact]
    public async Task Flags_low_balance_with_the_office_threshold()
    {
        SeedWallet(8_000_000);
        var settings = WalletSettings.CreateDefault(_tenantId);
        settings.ChangeLowBalanceThreshold(10_000_000, _userId, DateTime.UtcNow);
        _settings.Settings.Add(settings);

        var view = await HandleAsync();

        Assert.Equal(10_000_000, view.LowBalanceThresholdMicros);
        Assert.True(view.IsLowBalance);
        Assert.Equal(8_000_000, view.AvailableMicros);
    }

    [Fact]
    public async Task Turned_off_alert_never_flags()
    {
        SeedWallet(0);
        var settings = WalletSettings.CreateDefault(_tenantId);
        settings.ChangeLowBalanceThreshold(0, _userId, DateTime.UtcNow);
        _settings.Settings.Add(settings);

        var view = await HandleAsync();

        Assert.Equal(0, view.LowBalanceThresholdMicros);
        Assert.False(view.IsLowBalance);
    }

    [Fact]
    public async Task Reports_paused_auto_recharge_and_reason()
    {
        SeedWallet(20_000_000);
        var settings = WalletSettings.CreateDefault(_tenantId);
        settings.EnableAutoRecharge(
            2_500,
            5_000_000,
            AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value,
            new ConsentGrant(_userId, DateTime.UtcNow, "203.0.113.9", AutoRechargeConsentTemplate.Version, "ok")
        );
        settings.PauseAutoRecharge(AutoRechargePauseReason.PaymentFailed, DateTime.UtcNow);
        _settings.Settings.Add(settings);

        var view = await HandleAsync();

        Assert.Equal("Paused", view.AutoRechargeStatus);
        Assert.Equal("PaymentFailed", view.AutoRechargePausedReason);
        Assert.False(view.IsLowBalance);
    }

    [Fact]
    public async Task No_settings_row_uses_factory_defaults()
    {
        SeedWallet(4_000_000);

        var view = await HandleAsync();

        Assert.Equal(WalletSettings.DefaultLowBalanceThresholdMicros, view.LowBalanceThresholdMicros);
        Assert.True(view.IsLowBalance);
        Assert.Equal("Off", view.AutoRechargeStatus);
        Assert.Null(view.AutoRechargePausedReason);
        Assert.Empty(_settings.Settings);
    }

    [Fact]
    public async Task A_new_wallet_reports_the_signals_too()
    {
        var view = await HandleAsync();

        Assert.Equal(0, view.AvailableMicros);
        Assert.True(view.IsLowBalance);
        Assert.Single(_wallets.Wallets);
    }

    private void SeedWallet(long availableMicros)
    {
        var wallet = WalletAggregate.Create(_tenantId).Value;
        if (availableMicros > 0)
            wallet.Credit(Usd(availableMicros), "topup:seed");
        wallet.ClearDomainEvents();
        _wallets.Wallets.Add(wallet);
    }

    private async Task<WalletView> HandleAsync()
    {
        var result = await GetWalletHandler.Handle(
            new GetWalletQuery(_tenantId),
            _wallets,
            _settings,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
