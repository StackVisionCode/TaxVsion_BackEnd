using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>Nombre del emisor del certificado: el comercial del perfil si existe, si no el de la oficina.</summary>
public sealed class TenantBrandingRefTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Without_a_profile_the_issuer_is_the_office_name()
    {
        var branding = Branding("Acme Tax Office");

        Assert.Equal("Acme Tax Office", branding.IssuerDisplayName);
        Assert.Equal(0, branding.ProfileVersion);
    }

    [Fact]
    public void A_display_name_is_trimmed_and_used_as_the_issuer()
    {
        var branding = Branding("Acme Tax Office");

        Assert.True(branding.ApplyProfile(1, "  Acme Refunds  ", Now));

        Assert.Equal("Acme Refunds", branding.DisplayName);
        Assert.Equal("Acme Refunds", branding.IssuerDisplayName);
        Assert.Equal(1, branding.ProfileVersion);
    }

    [Fact]
    public void A_blank_display_name_falls_back_to_the_office_name()
    {
        var branding = Branding("Acme Tax Office");
        branding.ApplyProfile(1, "Acme Refunds", Now);

        Assert.True(branding.ApplyProfile(2, " ", Now));

        Assert.Null(branding.DisplayName);
        Assert.Equal("Acme Tax Office", branding.IssuerDisplayName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void A_profile_version_not_newer_than_the_applied_one_is_ignored(long version)
    {
        var branding = Branding("Acme Tax Office");
        branding.ApplyProfile(2, "Current", Now);

        Assert.False(branding.ApplyProfile(version, "Stale", Now));

        Assert.Equal("Current", branding.DisplayName);
        Assert.Equal(2, branding.ProfileVersion);
    }

    [Fact]
    public void Without_any_name_there_is_no_issuer()
    {
        var branding = TenantBrandingRef.Create(Guid.NewGuid(), Now);

        Assert.Null(branding.IssuerDisplayName);
    }

    private static TenantBrandingRef Branding(string officeName)
    {
        var branding = TenantBrandingRef.Create(Guid.NewGuid(), Now);
        branding.SetOfficeName(officeName, Now);
        return branding;
    }
}
