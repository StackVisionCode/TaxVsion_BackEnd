using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class OfficeProfileTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Update_normalizes_every_field_and_bumps_the_version()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);

        var result = profile.Update(OfficeProfileDrafts.Full(), ActorId, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(1, profile.Version);
        Assert.Equal(Now, profile.UpdatedAtUtc);
        Assert.Equal(ActorId, profile.UpdatedByUserId);
        Assert.Equal("Acme Tax Services LLC", profile.LegalName);
        Assert.Equal("Acme Tax", profile.DisplayName);
        Assert.Equal(OfficeEntityType.Llc, profile.EntityType);
        Assert.Equal("12-3456789", profile.Ein);
        Assert.Equal("(555) 123-4567", profile.Phone);
        Assert.Equal("https://acmetax.com", profile.Website);
        Assert.Equal("TX", profile.PhysicalAddress!.State);
        Assert.Equal("78701-1234", profile.PhysicalAddress.PostalCode);
        Assert.Equal("US", profile.PhysicalAddress.Country);
        Assert.Equal("PO Box 9", profile.MailingAddress!.Line1);
        Assert.Equal("es", profile.DefaultLanguage);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Saturday], profile.BusinessHours.Select(h => h.Day));
        Assert.Equal("https://g.page/r/acmetax/review", profile.ReviewUrl);
    }

    [Fact]
    public void Saving_the_same_state_again_changes_nothing()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);
        profile.Update(OfficeProfileDrafts.Full(), ActorId, Now);

        var result = profile.Update(OfficeProfileDrafts.Full(), Guid.NewGuid(), Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(1, profile.Version);
        Assert.Equal(Now, profile.UpdatedAtUtc);
        Assert.Equal(ActorId, profile.UpdatedByUserId);
    }

    [Fact]
    public void An_empty_draft_on_an_empty_profile_is_not_a_change()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);

        var result = profile.Update(OfficeProfileDrafts.Empty(), ActorId, Now);

        Assert.False(result.Value);
        Assert.Equal(0, profile.Version);
        Assert.Null(profile.UpdatedAtUtc);
    }

    [Fact]
    public void An_invalid_field_rejects_the_whole_save_and_leaves_the_profile_untouched()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);
        profile.Update(OfficeProfileDrafts.Full(), ActorId, Now);

        var result = profile.Update(
            OfficeProfileDrafts.Full() with
            {
                LegalName = "New Name LLC",
                Ein = "12-345",
            },
            ActorId,
            Now.AddMinutes(1)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Profile.Ein", result.Error.Code);
        Assert.Equal("Acme Tax Services LLC", profile.LegalName);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public void Mailing_same_as_physical_ignores_the_mailing_address()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);

        profile.Update(OfficeProfileDrafts.Full() with { MailingSameAsPhysical = true }, ActorId, Now);

        Assert.Null(profile.MailingAddress);
        Assert.NotNull(profile.PhysicalAddress);
    }

    [Fact]
    public void Changing_only_the_business_hours_is_a_change()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);
        profile.Update(OfficeProfileDrafts.Full(), ActorId, Now);

        var result = profile.Update(
            OfficeProfileDrafts.Full() with
            {
                BusinessHours = [new BusinessHoursDraft("Monday", "08:00", "18:00")],
            },
            ActorId,
            Now.AddMinutes(1)
        );

        Assert.True(result.Value);
        Assert.Equal(2, profile.Version);
        Assert.Single(profile.BusinessHours);
    }

    [Fact]
    public void The_updated_stamp_never_goes_backwards()
    {
        var profile = OfficeProfile.CreateEmpty(TenantId);
        profile.Update(OfficeProfileDrafts.Full(), ActorId, Now);

        profile.Update(OfficeProfileDrafts.Full() with { LegalName = "Other LLC" }, ActorId, Now.AddMinutes(-10));

        Assert.Equal(2, profile.Version);
        Assert.Equal(Now, profile.UpdatedAtUtc);
    }

    [Fact]
    public void Legacy_import_keeps_valid_fields_and_reports_each_dropped_one()
    {
        var draft = OfficeProfileDrafts.Empty() with
        {
            LegalName = "Legacy Tax Co",
            Ein = "not-an-ein",
            Phone = "123",
            Email = "office@legacytax.com",
            PhysicalAddress = new OfficeAddressDraft("1 Elm St", null, "Dallas", "Texas", "75201", "US"),
        };

        var import = OfficeProfile.ImportFromLegacy(TenantId, draft, ActorId, Now);

        Assert.NotNull(import.Profile);
        Assert.Equal(1, import.Profile.Version);
        Assert.Equal("Legacy Tax Co", import.Profile.LegalName);
        Assert.Null(import.Profile.Ein);
        Assert.Null(import.Profile.Phone);
        Assert.Equal("office@legacytax.com", import.Profile.Email);
        Assert.Equal("1 Elm St", import.Profile.PhysicalAddress!.Line1);
        Assert.Null(import.Profile.PhysicalAddress.State);
        Assert.Equal("75201", import.Profile.PhysicalAddress.PostalCode);
        Assert.Equal(
            ["Tenant.Profile.State", "Tenant.Profile.Ein", "Tenant.Profile.Phone"],
            import.DroppedFields.Select(e => e.Code)
        );
    }

    [Fact]
    public void Legacy_import_without_any_usable_value_yields_no_profile()
    {
        var import = OfficeProfile.ImportFromLegacy(
            TenantId,
            OfficeProfileDrafts.Empty() with
            {
                Ein = "bad",
            },
            ActorId,
            Now
        );

        Assert.Null(import.Profile);
        Assert.Single(import.DroppedFields);
    }

    [Fact]
    public void An_empty_tenant_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => OfficeProfile.CreateEmpty(Guid.Empty));
    }
}
