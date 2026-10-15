using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Billing;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Elección Automatic/Manual del tenant + evidencia legal (mandates) del consent.</summary>
public sealed class TenantBillingPreferenceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private const string ConsentText = "I authorize recurring charges for my TaxVision subscription.";
    private const string ConsentIp = "203.0.113.5";
    private const string ConsentUa = "Mozilla/5.0";

    private static Result<TenantBillingPreference> ChooseAutomatic() =>
        TenantBillingPreference.Choose(
            TenantId,
            BillingMode.Automatic,
            providerCode: "stripe",
            chosenByUserId: ActorId,
            consentText: ConsentText,
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            amountCents: 4900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now
        );

    private static Result<TenantBillingPreference> ChooseManual() =>
        TenantBillingPreference.Choose(
            TenantId,
            BillingMode.Manual,
            providerCode: null,
            chosenByUserId: ActorId,
            consentText: null,
            consentIpAddress: null,
            consentUserAgent: null,
            amountCents: 0,
            currency: string.Empty,
            cadence: string.Empty,
            nowUtc: Now
        );

    [Fact]
    public void Choose_automatic_creates_mandate_with_consent_evidence()
    {
        var result = ChooseAutomatic();

        Assert.True(result.IsSuccess);
        var preference = result.Value;
        Assert.Equal(BillingMode.Automatic, preference.Mode);
        Assert.Equal("stripe", preference.ProviderCode);
        Assert.Single(preference.Mandates);

        var mandate = preference.ActiveMandate();
        Assert.NotNull(mandate);
        Assert.Equal(ConsentText, mandate!.ConsentText);
        Assert.Equal(ConsentIp, mandate.ConsentIpAddress);
        Assert.Equal(4900, mandate.AgreedAmountCents);
        Assert.Equal("monthly", mandate.AgreedCadence);
        Assert.Equal(TenantPaymentMandate.CurrentConsentTextVersion, mandate.ConsentTextVersion);
        Assert.False(mandate.IsRevoked);
    }

    [Fact]
    public void Choose_manual_does_not_create_any_mandate()
    {
        var result = ChooseManual();

        Assert.True(result.IsSuccess);
        var preference = result.Value;
        Assert.Equal(BillingMode.Manual, preference.Mode);
        Assert.Null(preference.ProviderCode);
        Assert.Empty(preference.Mandates);
        Assert.Null(preference.ActiveMandate());
    }

    [Fact]
    public void Choose_automatic_without_consent_text_fails()
    {
        var result = TenantBillingPreference.Choose(
            TenantId,
            BillingMode.Automatic,
            providerCode: "stripe",
            chosenByUserId: ActorId,
            consentText: null,
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            amountCents: 4900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMandate.MissingConsentText", result.Error.Code);
    }

    [Fact]
    public void Choose_automatic_without_consent_ip_fails()
    {
        var result = TenantBillingPreference.Choose(
            TenantId,
            BillingMode.Automatic,
            providerCode: "stripe",
            chosenByUserId: ActorId,
            consentText: ConsentText,
            consentIpAddress: null,
            consentUserAgent: ConsentUa,
            amountCents: 4900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMandate.MissingConsentIp", result.Error.Code);
    }

    [Fact]
    public void SwitchToAutomatic_from_manual_creates_mandate_and_updates_mode()
    {
        var preference = ChooseManual().Value;

        var result = preference.SwitchToAutomatic(
            providerCode: "paypal",
            actorUserId: ActorId,
            consentText: ConsentText,
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            amountCents: 4900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now.AddMinutes(5)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingMode.Automatic, preference.Mode);
        Assert.Equal("paypal", preference.ProviderCode);
        Assert.Single(preference.Mandates);
        Assert.NotNull(preference.ActiveMandate());
        Assert.Equal("paypal", preference.ActiveMandate()!.ProviderCode);
    }

    [Fact]
    public void SwitchToManual_from_automatic_revokes_active_mandate()
    {
        var preference = ChooseAutomatic().Value;

        var result = preference.SwitchToManual(ActorId, Now.AddMinutes(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingMode.Manual, preference.Mode);
        Assert.Null(preference.ProviderCode);
        Assert.Null(preference.ActiveMandate());
        Assert.Single(preference.Mandates);
        Assert.True(preference.Mandates.Single().IsRevoked);
    }

    [Fact]
    public void RecordPriceChangeMandate_keeps_history_and_promotes_new_mandate()
    {
        var preference = ChooseAutomatic().Value;
        var originalMandate = preference.ActiveMandate()!;

        var result = preference.RecordPriceChangeMandate(
            actorUserId: ActorId,
            consentText: "I authorize the new $59.00 monthly charge.",
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            newAmountCents: 5900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now.AddDays(30)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, preference.Mandates.Count);
        Assert.True(originalMandate.IsRevoked);
        Assert.Equal("PriceChange", originalMandate.RevokedReason);

        var active = preference.ActiveMandate();
        Assert.NotNull(active);
        Assert.Equal(5900, active!.AgreedAmountCents);
        Assert.False(active.IsRevoked);
    }

    [Fact]
    public void ActiveMandate_returns_the_most_recent_non_revoked_mandate()
    {
        var preference = ChooseAutomatic().Value;
        preference.RecordPriceChangeMandate(
            ActorId,
            ConsentText,
            ConsentIp,
            ConsentUa,
            5900,
            "USD",
            "monthly",
            Now.AddDays(30)
        );
        preference.RecordPriceChangeMandate(
            ActorId,
            ConsentText,
            ConsentIp,
            ConsentUa,
            6900,
            "USD",
            "monthly",
            Now.AddDays(60)
        );

        var active = preference.ActiveMandate();

        Assert.NotNull(active);
        Assert.Equal(6900, active!.AgreedAmountCents);
        Assert.Equal(3, preference.Mandates.Count);
        Assert.Equal(2, preference.Mandates.Count(m => m.IsRevoked));
    }

    [Fact]
    public void SwitchToAutomatic_same_provider_is_idempotent()
    {
        var preference = ChooseAutomatic().Value;
        var originalMandateId = preference.ActiveMandate()!.Id;

        var result = preference.SwitchToAutomatic(
            providerCode: "stripe",
            actorUserId: ActorId,
            consentText: ConsentText,
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            amountCents: 4900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now.AddMinutes(5)
        );

        Assert.True(result.IsSuccess);
        Assert.Single(preference.Mandates);
        Assert.Equal(originalMandateId, preference.ActiveMandate()!.Id);
    }

    [Fact]
    public void SwitchToManual_when_already_manual_is_idempotent()
    {
        var preference = ChooseManual().Value;

        var result = preference.SwitchToManual(ActorId, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingMode.Manual, preference.Mode);
        Assert.Empty(preference.Mandates);
    }

    [Fact]
    public void SwitchToAutomatic_from_automatic_with_different_provider_revokes_old_and_creates_new()
    {
        var preference = ChooseAutomatic().Value;
        var originalMandateId = preference.ActiveMandate()!.Id;

        var result = preference.SwitchToAutomatic(
            providerCode: "paypal",
            actorUserId: ActorId,
            consentText: ConsentText,
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            amountCents: 4900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now.AddMinutes(5)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, preference.Mandates.Count);
        Assert.True(preference.Mandates.Single(m => m.Id == originalMandateId).IsRevoked);

        var active = preference.ActiveMandate();
        Assert.NotNull(active);
        Assert.Equal("paypal", active!.ProviderCode);
        Assert.NotEqual(originalMandateId, active.Id);
    }

    [Fact]
    public void RecordPriceChangeMandate_fails_when_mode_is_manual()
    {
        var preference = ChooseManual().Value;

        var result = preference.RecordPriceChangeMandate(
            actorUserId: ActorId,
            consentText: ConsentText,
            consentIpAddress: ConsentIp,
            consentUserAgent: ConsentUa,
            newAmountCents: 5900,
            currency: "USD",
            cadence: "monthly",
            nowUtc: Now.AddDays(30)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("BillingPreference.NotAutomatic", result.Error.Code);
    }
}
