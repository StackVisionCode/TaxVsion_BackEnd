using TaxVision.Wallet.Application.Settings.Commands;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Tests.TestDoubles;

namespace TaxVision.Wallet.Tests.Application;

public sealed class EnableAutoRechargeHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeWalletSettingsRepository _settings = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Handle_ConsentAccepted_BuildsTheTextOnTheServerAndSavesTheConsent()
    {
        var result = await HandleAsync(Command());

        Assert.True(result.IsSuccess);
        var settings = Assert.Single(_settings.Settings);
        Assert.Equal(AutoRechargeStatus.Active, settings.AutoRechargeStatus);
        Assert.Equal(_userId, settings.ConsentAcceptedByUserId);
        Assert.Equal("198.51.100.7", settings.ConsentIpAddress);
        Assert.Equal(AutoRechargeConsentTemplate.Version, settings.ConsentTextVersion);
        Assert.StartsWith("I authorize Acme Cloud to charge $25.00 to Visa 4242", settings.ConsentText);
        Assert.Equal(WalletSettingsAction.AutoRechargeEnabled, Assert.Single(_settings.Changes).Action);
        Assert.Equal("Active", result.Value.AutoRecharge.Status);
        Assert.Equal(settings.ConsentText, result.Value.AutoRecharge.Consent!.Text);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_ConsentNotAccepted_IsRejectedWithoutSaving()
    {
        var result = await HandleAsync(Command() with { ConsentAccepted = false });

        Assert.Equal(WalletSettingsErrors.ConsentRequired, result.Error);
        Assert.Empty(_settings.Settings);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_OldConsentVersion_IsAConflict()
    {
        var result = await HandleAsync(Command() with { ConsentTextVersion = "2020-01-01" });

        Assert.Equal(WalletSettingsErrors.ConsentVersionConflict, result.Error);
        Assert.Empty(_settings.Settings);
    }

    [Fact]
    public async Task Handle_InvalidAmount_DoesNotSave()
    {
        var result = await HandleAsync(Command() with { AmountCents = 500 });

        Assert.Equal(WalletSettingsErrors.AmountNotAllowed, result.Error);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private EnableAutoRechargeCommand Command() =>
        new(
            _tenantId,
            _userId,
            AmountCents: 2_500,
            ThresholdMicros: 5_000_000,
            Provider: "Stripe",
            MethodReference: "pm_1",
            MethodLabel: "Visa 4242",
            ConsentAccepted: true,
            ConsentTextVersion: AutoRechargeConsentTemplate.Version,
            IpAddress: "198.51.100.7"
        );

    private Task<BuildingBlocks.Results.Result<TaxVision.Wallet.Application.Settings.WalletSettingsView>> HandleAsync(
        EnableAutoRechargeCommand command
    ) =>
        EnableAutoRechargeHandler.Handle(
            command,
            _settings,
            new FakeOfficeTimeZone(),
            new FakePlatformSettings("Acme Cloud"),
            _unitOfWork,
            CancellationToken.None
        );
}
