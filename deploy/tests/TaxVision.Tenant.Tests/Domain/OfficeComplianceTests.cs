using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.ValueObjects;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class OfficeComplianceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private static readonly OfficeComplianceDraft EmptyDraft = new(null, null, null, null, null, null);

    [Fact]
    public void Update_normalizes_the_details_and_stamps_who_changed_them()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        var coordinator = Guid.NewGuid();

        var result = compliance.Update(
            new OfficeComplianceDraft(coordinator, "2025-01-15", "2026-01-10", " security@acme.com ", "5551234567", 7),
            EfinChange.Keep,
            ActorId,
            Now
        );

        Assert.True(result.Value);
        Assert.Equal(coordinator, compliance.SecurityCoordinatorUserId);
        Assert.Equal(new DateOnly(2025, 1, 15), compliance.WispAdoptedOn);
        Assert.Equal(new DateOnly(2026, 1, 10), compliance.WispLastReviewedOn);
        Assert.Equal("security@acme.com", compliance.IncidentContactEmail);
        Assert.Equal("(555) 123-4567", compliance.IncidentContactPhone);
        Assert.Equal(7, compliance.RetentionYears);
        Assert.Equal(Now, compliance.UpdatedAtUtc);
        Assert.Equal(ActorId, compliance.UpdatedByUserId);
    }

    [Fact]
    public void Replacing_and_clearing_the_efin_are_changes_keeping_it_is_not()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        var encrypted = TestEfinProtectors.Create(out _).Protect(Efin.Create("123456").Value);

        Assert.True(compliance.Update(EmptyDraft, EfinChange.Replace(encrypted), ActorId, Now).Value);
        Assert.Equal("••••56", compliance.Efin!.Masked);

        Assert.False(compliance.Update(EmptyDraft, EfinChange.Keep, ActorId, Now).Value);

        Assert.True(compliance.Update(EmptyDraft, EfinChange.Clear, ActorId, Now).Value);
        Assert.Null(compliance.Efin);
        Assert.False(compliance.Update(EmptyDraft, EfinChange.Clear, ActorId, Now).Value);
    }

    [Theory]
    [InlineData("2026-11-05", null, null, "Tenant.Compliance.WispAdoptedOn")]
    [InlineData("01/15/2025", null, null, "Tenant.Compliance.WispAdoptedOn")]
    [InlineData("2025-06-01", "2025-05-01", null, "Tenant.Compliance.WispLastReviewedOn")]
    [InlineData(null, "2027-01-01", null, "Tenant.Compliance.WispLastReviewedOn")]
    [InlineData(null, null, 2, "Tenant.Compliance.RetentionYears")]
    [InlineData(null, null, 21, "Tenant.Compliance.RetentionYears")]
    public void Invalid_dates_or_retention_are_rejected_without_changes(
        string? adopted,
        string? reviewed,
        int? retention,
        string expectedCode
    )
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        var encrypted = TestEfinProtectors.Create(out _).Protect(Efin.Create("123456").Value);

        var result = compliance.Update(
            new OfficeComplianceDraft(null, adopted, reviewed, null, null, retention),
            EfinChange.Replace(encrypted),
            ActorId,
            Now
        );

        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Null(compliance.Efin);
        Assert.Null(compliance.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(20)]
    public void Retention_accepts_three_to_twenty_years(int years)
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);

        var result = compliance.Update(EmptyDraft with { RetentionYears = years }, EfinChange.Keep, ActorId, Now);

        Assert.True(result.Value);
        Assert.Equal(years, compliance.RetentionYears);
    }

    [Fact]
    public void Retention_out_of_range_names_the_new_limits()
    {
        var result = OfficeCompliance
            .CreateEmpty(TenantId)
            .Update(EmptyDraft with { RetentionYears = 21 }, EfinChange.Keep, ActorId, Now);

        Assert.Equal("Retention must be between 3 and 20 years.", result.Error.Message);
    }

    [Fact]
    public void Today_in_the_most_eastern_time_zone_is_not_a_future_date()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);

        var result = compliance.Update(
            EmptyDraft with
            {
                WispAdoptedOn = "2026-11-04",
            },
            EfinChange.Keep,
            ActorId,
            new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc)
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Invalid_incident_contact_uses_the_compliance_codes()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);

        var email = compliance.Update(EmptyDraft with { IncidentContactEmail = "nope" }, EfinChange.Keep, ActorId, Now);
        var phone = compliance.Update(EmptyDraft with { IncidentContactPhone = "12" }, EfinChange.Keep, ActorId, Now);

        Assert.Equal("Tenant.Compliance.IncidentContactEmail", email.Error.Code);
        Assert.Equal("Tenant.Compliance.IncidentContactPhone", phone.Error.Code);
    }

    [Fact]
    public void Offboarding_the_coordinator_vacates_the_designation_and_stamps_who()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        var coordinator = Guid.NewGuid();
        compliance.Update(EmptyDraft with { SecurityCoordinatorUserId = coordinator }, EfinChange.Keep, ActorId, Now);
        var offboardedBy = Guid.NewGuid();
        var later = Now.AddDays(1);

        Assert.True(compliance.VacateSecurityCoordinatorOnOffboarding(coordinator, offboardedBy, later));

        Assert.Null(compliance.SecurityCoordinatorUserId);
        Assert.Equal(later, compliance.UpdatedAtUtc);
        Assert.Equal(offboardedBy, compliance.UpdatedByUserId);
        Assert.False(compliance.VacateSecurityCoordinatorOnOffboarding(coordinator, offboardedBy, later));
    }

    [Fact]
    public void Offboarding_someone_else_leaves_the_coordinator_untouched()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        var coordinator = Guid.NewGuid();
        compliance.Update(EmptyDraft with { SecurityCoordinatorUserId = coordinator }, EfinChange.Keep, ActorId, Now);

        Assert.False(compliance.VacateSecurityCoordinatorOnOffboarding(Guid.NewGuid(), ActorId, Now.AddDays(1)));
        Assert.False(compliance.VacateSecurityCoordinatorOnOffboarding(Guid.Empty, ActorId, Now.AddDays(1)));

        Assert.Equal(coordinator, compliance.SecurityCoordinatorUserId);
        Assert.Equal(Now, compliance.UpdatedAtUtc);
    }

    [Fact]
    public void Revealing_needs_an_efin_and_records_who_and_when()
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        Assert.Equal(OfficeComplianceErrors.EfinNotSet, compliance.RecordEfinReveal(ActorId, Now).Error);

        var encrypted = TestEfinProtectors.Create(out _).Protect(Efin.Create("123456").Value);
        compliance.Update(EmptyDraft, EfinChange.Replace(encrypted), ActorId, Now);

        var record = compliance.RecordEfinReveal(ActorId, Now).Value;
        Assert.Equal(TenantId, record.TenantId);
        Assert.Equal(ActorId, record.RevealedByUserId);
        Assert.Equal(Now, record.RevealedAtUtc);
    }
}
