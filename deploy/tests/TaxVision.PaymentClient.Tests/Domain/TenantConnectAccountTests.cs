using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class TenantConnectAccountTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    [Fact]
    public void Create_with_an_empty_tenant_fails()
    {
        var result = TenantConnectAccount.Create(
            Guid.Empty,
            PaymentProviderCode.Stripe,
            ConnectAccountType.Standard,
            AccountId("acct_1"),
            NowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantConnectAccount.InvalidTenant", result.Error.Code);
    }

    [Fact]
    public void Create_starts_Pending_and_cannot_charge()
    {
        var account = CreateAccount();

        Assert.Equal(ConnectAccountStatus.Pending, account.Status);
        Assert.Equal(OnboardingStep.NotStarted, account.OnboardingStep);
        Assert.False(account.CanCharge);
    }

    [Fact]
    public void InitiateOnboarding_moves_Pending_to_InProgress()
    {
        var account = CreateAccount();

        var result = account.InitiateOnboarding(NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectAccountStatus.InProgress, account.Status);
        Assert.Equal(OnboardingStep.LinkGenerated, account.OnboardingStep);
    }

    [Fact]
    public void UpdateFromWebhook_with_charges_enabled_and_no_requirements_reaches_Enabled()
    {
        var account = CreateAccount();
        account.InitiateOnboarding(NowUtc);

        var result = account.UpdateFromWebhook(
            chargesEnabled: true,
            payoutsEnabled: true,
            requirementsCurrentlyDue: [],
            NowUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectAccountStatus.Enabled, account.Status);
        Assert.Equal(OnboardingStep.Completed, account.OnboardingStep);
        Assert.True(account.CanCharge);
    }

    [Fact]
    public void UpdateFromWebhook_with_pending_requirements_stays_below_Enabled()
    {
        var account = CreateAccount();
        account.InitiateOnboarding(NowUtc);

        var result = account.UpdateFromWebhook(
            chargesEnabled: true,
            payoutsEnabled: false,
            requirementsCurrentlyDue: ["individual.verification.document"],
            NowUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectAccountStatus.InProgress, account.Status);
    }

    [Fact]
    public void An_enabled_account_that_loses_charges_enabled_becomes_Restricted()
    {
        var account = CreateAccount();
        account.InitiateOnboarding(NowUtc);
        account.UpdateFromWebhook(true, true, [], NowUtc);

        var result = account.UpdateFromWebhook(
            chargesEnabled: false,
            payoutsEnabled: true,
            requirementsCurrentlyDue: ["individual.verification.document"],
            NowUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectAccountStatus.Restricted, account.Status);
        Assert.False(account.CanCharge);
    }

    [Fact]
    public void A_restricted_account_that_recovers_charges_enabled_becomes_Enabled_again()
    {
        var account = CreateAccount();
        account.InitiateOnboarding(NowUtc);
        account.UpdateFromWebhook(true, true, [], NowUtc);
        account.UpdateFromWebhook(false, true, ["individual.verification.document"], NowUtc);

        var result = account.UpdateFromWebhook(
            chargesEnabled: true,
            payoutsEnabled: true,
            requirementsCurrentlyDue: [],
            NowUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectAccountStatus.Enabled, account.Status);
    }

    [Fact]
    public void Deactivate_with_no_reason_fails()
    {
        var account = CreateAccount();

        var result = account.Deactivate("  ", Guid.Empty, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantConnectAccount.InvalidReason", result.Error.Code);
    }

    [Fact]
    public void Deactivate_disables_the_account_and_revokes_charging()
    {
        var account = CreateAccount();
        account.InitiateOnboarding(NowUtc);
        account.UpdateFromWebhook(true, true, [], NowUtc);

        var result = account.Deactivate("fraud review", Guid.Empty, NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectAccountStatus.Disabled, account.Status);
        Assert.False(account.CanCharge);
    }

    [Fact]
    public void UpdateFromWebhook_on_a_disabled_account_is_rejected()
    {
        var account = CreateAccount();
        account.Deactivate("fraud review", Guid.Empty, NowUtc);

        var result = account.UpdateFromWebhook(true, true, [], NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantConnectAccount.InvalidTransition", result.Error.Code);
    }

    private static StripeConnectAccountId AccountId(string value) => StripeConnectAccountId.Create(value).Value;

    private static TenantConnectAccount CreateAccount() =>
        TenantConnectAccount
            .Create(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                ConnectAccountType.Standard,
                AccountId("acct_1"),
                NowUtc
            )
            .Value;
}
