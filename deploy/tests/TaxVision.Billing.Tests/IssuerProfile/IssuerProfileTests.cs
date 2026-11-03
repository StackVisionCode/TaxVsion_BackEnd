using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.IssuerProfile;

public sealed class IssuerProfileTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static TenantOfficeProfile Source(
        string? legalName = "Gutierrez Tax Office LLC",
        string? displayName = "Gutierrez Tax",
        string? taxId = "12-3456789",
        Address? address = null,
        string? phone = "(512) 555-0112",
        string? email = "office@gutierreztax.com",
        string? website = "https://gutierreztax.com"
    ) => new(legalName, displayName, taxId, address, phone, email, website);

    [Fact]
    public void The_legal_name_wins_and_every_field_is_copied()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        var address = new Address("1600 Main St", "Suite 200", "Austin", "TX", "78701", "US");

        Assert.True(profile.ApplyTenantProfile(1, Source(address: address), Now));

        Assert.Equal("Gutierrez Tax Office LLC", profile.Name);
        Assert.Equal("12-3456789", profile.TaxId);
        Assert.Equal(address, profile.Address);
        Assert.Equal("(512) 555-0112", profile.Phone);
        Assert.Equal("office@gutierreztax.com", profile.Email);
        Assert.Equal("https://gutierreztax.com", profile.Website);
        Assert.Equal(1, profile.SourceVersion);
    }

    [Fact]
    public void Without_a_legal_name_the_display_name_is_used()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);

        profile.ApplyTenantProfile(1, Source(legalName: " ", displayName: "Gutierrez Tax"), Now);

        Assert.Equal("Gutierrez Tax", profile.Name);
    }

    [Fact]
    public void Without_any_name_the_current_one_is_kept()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        profile.ApplyTenantProfile(1, Source(), Now);

        profile.ApplyTenantProfile(2, Source(legalName: null, displayName: null), Now);

        Assert.Equal("Gutierrez Tax Office LLC", profile.Name);
    }

    [Fact]
    public void A_new_profile_without_any_name_stays_unusable()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);

        profile.ApplyTenantProfile(1, Source(legalName: null, displayName: null), Now);

        Assert.False(profile.IsUsable);
        Assert.Equal("12-3456789", profile.TaxId);
    }

    [Fact]
    public void Missing_values_clear_the_previous_ones()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        profile.ApplyTenantProfile(1, Source(address: new Address("1 Main", null, "Austin", "TX", "78701", "US")), Now);

        profile.ApplyTenantProfile(2, Source(taxId: null, phone: "", email: null, website: null), Now);

        Assert.Null(profile.TaxId);
        Assert.Null(profile.Phone);
        Assert.Null(profile.Email);
        Assert.Null(profile.Website);
        Assert.Null(profile.Address);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void An_older_or_repeated_version_changes_nothing(long version)
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        profile.ApplyTenantProfile(3, Source(), Now);

        var applied = profile.ApplyTenantProfile(version, Source(legalName: "Stale name"), Now);

        Assert.False(applied);
        Assert.Equal("Gutierrez Tax Office LLC", profile.Name);
        Assert.Equal(3, profile.SourceVersion);
    }

    [Fact]
    public void A_malformed_legacy_value_is_kept_and_an_overlong_one_is_cut_never_rejected()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        var longName = new string('a', DomainIssuerProfile.NameMaxLength + 20);

        var applied = profile.ApplyTenantProfile(1, Source(legalName: longName, taxId: "12 345", email: "x"), Now);

        Assert.True(applied);
        Assert.Equal(DomainIssuerProfile.NameMaxLength, profile.Name.Length);
        Assert.Equal("12 345", profile.TaxId);
        Assert.Equal("x", profile.Email);
    }

    [Fact]
    public void The_logo_and_the_currency_are_not_touched()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        var logo = Guid.NewGuid();
        // El logo no tiene escritura pública hoy: se siembra como lo haría una fila ya guardada.
        typeof(DomainIssuerProfile).GetProperty(nameof(DomainIssuerProfile.LogoFileId))!.SetValue(profile, logo);

        profile.ApplyTenantProfile(1, Source(), Now);

        Assert.Equal(logo, profile.LogoFileId);
        Assert.Equal("USD", profile.DefaultCurrency);
    }

    [Fact]
    public void Without_an_address_the_snapshot_carries_an_empty_one_and_the_contact_details()
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        profile.ApplyTenantProfile(1, Source(), Now);

        var snapshot = profile.ToSnapshot();

        Assert.Null(snapshot.Address.ToSingleLine());
        Assert.Equal("(512) 555-0112", snapshot.Phone);
        Assert.Equal("office@gutierreztax.com", snapshot.Email);
        Assert.Equal("https://gutierreztax.com", snapshot.Website);
        Assert.Equal("12-3456789", snapshot.TaxId);
    }
}
