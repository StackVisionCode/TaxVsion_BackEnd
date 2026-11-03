using TaxVision.Signature.Domain.Settings;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

public sealed class TenantSignatureSettingsTests
{
    private static TenantSignatureSettings New() =>
        TenantSignatureSettings.CreateForNewTenant(Guid.NewGuid(), "encrypted-secret").Value;

    [Fact]
    public void New_tenant_allows_employee_own_signature_by_default()
    {
        var settings = New();

        Assert.True(settings.AllowEmployeeOwnSignature);
    }

    [Fact]
    public void Disable_forces_office_signature()
    {
        var settings = New();

        settings.DisableEmployeeOwnSignature();

        Assert.False(settings.AllowEmployeeOwnSignature);
    }

    [Fact]
    public void Enable_after_disable_restores_own_signature()
    {
        var settings = New();
        settings.DisableEmployeeOwnSignature();

        settings.EnableEmployeeOwnSignature();

        Assert.True(settings.AllowEmployeeOwnSignature);
    }

    // ---- Retención: la oficina (Cumplimiento) manda; el plan es el piso ----

    private static readonly DateTime At = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static SignatureLimits Plan(int minRetentionYears = 3, bool purgeAllowed = true) =>
        SignatureLimits.Create(
            20,
            50,
            25L * 1024 * 1024,
            100,
            720,
            minRetentionYears,
            purgeAllowed,
            VerificationChannel.Email
        );

    [Fact]
    public void Without_office_retention_the_signature_years_rule()
    {
        var settings = New();
        settings.ReplaceRetentionPolicy(RetentionPolicy.Default().WithYears(9).Value);

        var effective = settings.EffectiveWithin(Plan());

        Assert.Equal(9, effective.RetentionYears);
        Assert.Equal(RetentionYearsSource.Signature, effective.RetentionSource);
    }

    [Fact]
    public void A_new_office_without_any_retention_keeps_the_default_of_seven()
    {
        var effective = New().EffectiveWithin(Plan());

        Assert.Equal(RetentionPolicy.DefaultRetentionYears, effective.RetentionYears);
        Assert.Equal(RetentionYearsSource.Signature, effective.RetentionSource);
    }

    [Fact]
    public void The_office_retention_wins_over_the_signature_years()
    {
        var settings = New();
        settings.ReplaceRetentionPolicy(RetentionPolicy.Default().WithYears(9).Value);

        settings.ApplyOfficeRetention(4, At);
        var effective = settings.EffectiveWithin(Plan());

        Assert.Equal(4, effective.RetentionYears);
        Assert.Equal(RetentionYearsSource.Office, effective.RetentionSource);
    }

    [Fact]
    public void The_plan_minimum_is_still_the_floor_of_the_office_retention()
    {
        var settings = New();
        settings.ApplyOfficeRetention(4, At);

        var effective = settings.EffectiveWithin(Plan(minRetentionYears: 10));

        Assert.Equal(10, effective.RetentionYears);
    }

    [Fact]
    public void The_platform_minimum_also_floors_the_office_retention()
    {
        var settings = New();
        settings.ApplyOfficeRetention(4, At);
        settings.ApplyPlanConstraints(
            SignaturePlanConstraints.Create(null, null, null, null, 15, null, null, null).Value
        );

        Assert.Equal(15, settings.EffectiveWithin(Plan()).RetentionYears);
    }

    [Fact]
    public void Clearing_the_office_retention_falls_back_to_the_signature_years()
    {
        var settings = New();
        settings.ApplyOfficeRetention(12, At);

        settings.ApplyOfficeRetention(null, At.AddMinutes(1));
        var effective = settings.EffectiveWithin(Plan());

        Assert.Equal(RetentionPolicy.DefaultRetentionYears, effective.RetentionYears);
        Assert.Equal(RetentionYearsSource.Signature, effective.RetentionSource);
    }

    [Fact]
    public void An_older_office_retention_change_is_ignored()
    {
        var settings = New();
        settings.ApplyOfficeRetention(12, At);

        var older = settings.ApplyOfficeRetention(5, At.AddMinutes(-1));

        Assert.False(older.Value);
        Assert.Equal(12, settings.OfficeRetentionYears);
        Assert.Equal(At, settings.OfficeRetentionUpdatedAtUtc);
    }

    [Fact]
    public void Repeating_the_same_office_retention_change_changes_nothing()
    {
        var settings = New();
        settings.ApplyOfficeRetention(12, At);
        var touched = settings.UpdatedAtUtc;

        var again = settings.ApplyOfficeRetention(12, At);

        Assert.False(again.Value);
        Assert.Equal(touched, settings.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void An_office_retention_out_of_range_is_rejected(int years)
    {
        var settings = New();

        var result = settings.ApplyOfficeRetention(years, At);

        Assert.True(result.IsFailure);
        Assert.Null(settings.OfficeRetentionYears);
    }

    [Fact]
    public void Purge_stays_opt_in_and_toggling_it_keeps_the_years()
    {
        var settings = New();
        settings.ApplyOfficeRetention(12, At);
        Assert.False(settings.EffectiveWithin(Plan()).PurgeEnabled);

        settings.SetPurgeAllowed(true);

        Assert.True(settings.Retention.AllowPurge);
        Assert.Equal(RetentionPolicy.DefaultRetentionYears, settings.Retention.RetentionYears);
        Assert.True(settings.EffectiveWithin(Plan()).PurgeEnabled);
        Assert.False(settings.EffectiveWithin(Plan(purgeAllowed: false)).PurgeEnabled);
        Assert.Equal(12, settings.EffectiveWithin(Plan()).RetentionYears);
    }
}
