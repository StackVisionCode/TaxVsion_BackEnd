using TaxVision.Wallet.Application.Settings.Commands;
using TaxVision.Wallet.Application.Settings.Queries;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Application;

public sealed class WalletSettingsQueriesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeWalletSettingsRepository _settings = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task GetWalletSettings_NoRow_ReturnsDefaultsAndTheTemplateWithoutCreatingIt()
    {
        var view = await GetWalletSettingsHandler.Handle(
            new GetWalletSettingsQuery(_tenantId),
            _settings,
            new FakeOfficeTimeZone(),
            CancellationToken.None
        );

        Assert.Equal(5_000_000, view.LowBalanceThresholdMicros);
        Assert.Equal("Off", view.AutoRecharge.Status);
        Assert.Equal(3, view.AutoRecharge.DailyLimit);
        Assert.Equal(5_000_000, view.AutoRecharge.ThresholdMicros);
        Assert.Null(view.AutoRecharge.Consent);
        Assert.Equal(AutoRechargeConsentTemplate.Version, view.ConsentTemplate.Version);
        Assert.Contains("{platform}", view.ConsentTemplate.Text);
        Assert.Empty(_settings.Settings);
    }

    [Fact]
    public async Task ChangeThresholdAndDisable_LeaveHistoryNewestFirst()
    {
        await ChangeLowBalanceThresholdHandler.Handle(
            new ChangeLowBalanceThresholdCommand(_tenantId, _userId, 10_000_000),
            _settings,
            new FakeOfficeTimeZone(),
            _unitOfWork,
            CancellationToken.None
        );
        var settings = Assert.Single(_settings.Settings);
        settings.EnableAutoRecharge(
            2_500,
            5_000_000,
            AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value,
            new ConsentGrant(_userId, DateTime.UtcNow.AddSeconds(1), null, AutoRechargeConsentTemplate.Version, "ok")
        );
        var off = await DisableAutoRechargeHandler.Handle(
            new DisableAutoRechargeCommand(_tenantId, _userId),
            _settings,
            new FakeOfficeTimeZone(),
            _unitOfWork,
            CancellationToken.None
        );

        var history = await ListWalletSettingsHistoryHandler.Handle(
            new ListWalletSettingsHistoryQuery(_tenantId, 1, 20),
            _settings,
            CancellationToken.None
        );

        Assert.Equal("Off", off.Value.AutoRecharge.Status);
        Assert.Equal(["AutoRechargeDisabled", "LowBalanceThresholdChanged"], history.Select(h => h.Action).ToArray());
        Assert.Equal("$5.00 → $10.00", history[1].Detail);
        Assert.Equal(_userId, history[1].UserId);
    }
}
