using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class WalletSettingsTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void CreateDefault_FiveDollarThresholdAndAutoRechargeOff()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());

        Assert.Equal(5_000_000, settings.LowBalanceThresholdMicros);
        Assert.Equal(AutoRechargeStatus.Off, settings.AutoRechargeStatus);
        Assert.Equal(0, settings.RechargesOn(Today));
    }

    [Fact]
    public void CheckBalance_AlertsOncePerCrossingAndRearmsWhenBackAbove()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());

        var first = settings.CheckBalance(4_000_000, Today, pendingAutomatic: false);
        var second = settings.CheckBalance(3_000_000, Today, pendingAutomatic: false);
        var rearm = settings.CheckBalance(6_000_000, Today, pendingAutomatic: false);
        var again = settings.CheckBalance(1_000_000, Today, pendingAutomatic: false);

        Assert.True(first.LowBalanceAlert && first.Changed);
        Assert.False(second.LowBalanceAlert || second.Changed);
        Assert.True(!rearm.LowBalanceAlert && rearm.Changed);
        Assert.True(again.LowBalanceAlert);
    }

    [Fact]
    public void CheckBalance_ZeroThreshold_NeverAlerts()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());
        settings.ChangeLowBalanceThreshold(0, _userId, DateTime.UtcNow);

        Assert.False(settings.CheckBalance(0, Today, pendingAutomatic: false).LowBalanceAlert);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    public void ChangeLowBalanceThreshold_OutOfRange_IsRejected(long micros)
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());

        var result = settings.ChangeLowBalanceThreshold(micros, _userId, DateTime.UtcNow);

        Assert.Equal(WalletSettingsErrors.ThresholdOutOfRange, result.Error);
        Assert.Equal(5_000_000, settings.LowBalanceThresholdMicros);
    }

    [Fact]
    public void ChangeLowBalanceThreshold_RecordsTheChange()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());

        var change = settings.ChangeLowBalanceThreshold(10_000_000, _userId, DateTime.UtcNow).Value;

        Assert.Equal(10_000_000, settings.LowBalanceThresholdMicros);
        Assert.Equal(WalletSettingsAction.LowBalanceThresholdChanged, change.Action);
        Assert.Equal(_userId, change.UserId);
        Assert.Equal("$5.00 → $10.00", change.Detail);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(100_001)]
    public void EnableAutoRecharge_AmountOutOfRange_IsRejected(long amountCents)
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());

        var result = settings.EnableAutoRecharge(amountCents, 5_000_000, Method(), Consent());

        Assert.Equal(WalletSettingsErrors.AmountNotAllowed, result.Error);
        Assert.Equal(AutoRechargeStatus.Off, settings.AutoRechargeStatus);
    }

    [Fact]
    public void EnableAutoRecharge_SavesTheConsentAndTurnsItOn()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());
        var consent = Consent();

        var change = settings.EnableAutoRecharge(2_500, 5_000_000, Method(), consent).Value;

        Assert.Equal(AutoRechargeStatus.Active, settings.AutoRechargeStatus);
        Assert.Equal(2_500, settings.AutoRechargeAmountCents);
        Assert.Equal("pm_1", settings.AutoRechargeMethodReference);
        Assert.Equal(_userId, settings.ConsentAcceptedByUserId);
        Assert.Equal("203.0.113.9", settings.ConsentIpAddress);
        Assert.Equal(consent.Text, settings.ConsentText);
        Assert.Equal(WalletSettingsAction.AutoRechargeEnabled, change.Action);
        Assert.Equal(consent.Text, change.ConsentText);
        Assert.Equal("203.0.113.9", change.IpAddress);
    }

    [Fact]
    public void CheckBalance_ActiveBelowThreshold_RechargesUpToThreeTimesADayAndWarnsOnce()
    {
        var settings = Active();

        var decisions = Enumerable
            .Range(0, 5)
            .Select(_ => settings.CheckBalance(1_000_000, Today, pendingAutomatic: false).AutoRecharge)
            .ToList();
        var nextDay = settings.CheckBalance(1_000_000, Today.AddDays(1), pendingAutomatic: false);

        Assert.Equal(
            [
                AutoRechargeDecision.Recharge,
                AutoRechargeDecision.Recharge,
                AutoRechargeDecision.Recharge,
                AutoRechargeDecision.DailyLimitReached,
                AutoRechargeDecision.None,
            ],
            decisions
        );
        Assert.Equal(AutoRechargeDecision.Recharge, nextDay.AutoRecharge);
        Assert.Equal(1, settings.RechargesOn(Today.AddDays(1)));
    }

    [Fact]
    public void CheckBalance_PendingAutomaticOrAboveThreshold_DoesNotRecharge()
    {
        var settings = Active();

        Assert.Equal(
            AutoRechargeDecision.None,
            settings.CheckBalance(1_000_000, Today, pendingAutomatic: true).AutoRecharge
        );
        Assert.Equal(
            AutoRechargeDecision.None,
            settings.CheckBalance(9_000_000, Today, pendingAutomatic: false).AutoRecharge
        );
        Assert.Equal(0, settings.RechargesOn(Today));
    }

    [Fact]
    public void PauseAutoRecharge_StopsRechargesUntilEnabledAgain()
    {
        var settings = Active();

        var pause = settings.PauseAutoRecharge(AutoRechargePauseReason.MethodUnavailable, DateTime.UtcNow);
        var paused = settings.CheckBalance(1_000_000, Today, pendingAutomatic: false);
        settings.EnableAutoRecharge(2_500, 5_000_000, Method(), Consent());

        Assert.Equal(WalletSettingsAction.AutoRechargePaused, pause!.Action);
        Assert.Null(pause.UserId);
        Assert.Equal(AutoRechargeDecision.None, paused.AutoRecharge);
        Assert.Equal(AutoRechargeStatus.Active, settings.AutoRechargeStatus);
        Assert.Null(settings.AutoRechargePausedReason);
    }

    [Fact]
    public void PauseAndDisable_OnlyChangeWhatIsOn()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());

        Assert.Null(settings.PauseAutoRecharge(AutoRechargePauseReason.PaymentFailed, DateTime.UtcNow));
        Assert.Null(settings.DisableAutoRecharge(_userId, DateTime.UtcNow));

        settings.EnableAutoRecharge(2_500, 5_000_000, Method(), Consent());
        var off = settings.DisableAutoRecharge(_userId, DateTime.UtcNow);

        Assert.Equal(WalletSettingsAction.AutoRechargeDisabled, off!.Action);
        Assert.Equal(AutoRechargeStatus.Off, settings.AutoRechargeStatus);
    }

    [Fact]
    public void ConsentTemplate_RendersAmountMethodThresholdAndDailyMaximum()
    {
        var text = AutoRechargeConsentTemplate.Render("TaxOffix", 2_500, "Visa 4242", 5_000_000);

        Assert.Equal(
            "I authorize TaxOffix to charge $25.00 to Visa 4242 each time the wallet balance drops below $5.00, "
                + "up to 3 times a day ($75.00). Each charge is emailed as a receipt. I can turn this off at any time.",
            text
        );
    }

    private WalletSettings Active()
    {
        var settings = WalletSettings.CreateDefault(Guid.NewGuid());
        settings.EnableAutoRecharge(2_500, 5_000_000, Method(), Consent());
        return settings;
    }

    private static AutoRechargeMethod Method() => AutoRechargeMethod.Create("Stripe", "pm_1", "Visa 4242").Value;

    private ConsentGrant Consent() =>
        new(_userId, DateTime.UtcNow, "203.0.113.9", AutoRechargeConsentTemplate.Version, "I authorize ...");
}
