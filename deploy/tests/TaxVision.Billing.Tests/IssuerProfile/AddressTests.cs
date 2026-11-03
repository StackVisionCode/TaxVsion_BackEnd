using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.IssuerProfile;

public sealed class AddressTests
{
    [Fact]
    public void Parts_are_trimmed_and_kept_without_validation()
    {
        // Tenant ya validó; un valor raro de antes se conserva tal cual en vez de romper la proyección.
        var address = Address.FromParts(null, null, "  ", " Texas ", "1", null);

        Assert.NotNull(address);
        Assert.Equal("Texas", address.State);
        Assert.Equal("1", address.Zip);
        Assert.Equal("US", address.Country);
        Assert.Equal(string.Empty, address.Line1);
    }

    [Fact]
    public void Suite_or_unit_is_kept_and_blank_becomes_null()
    {
        var withSuite = Address.FromParts("1600 Main St", " Suite 200 ", "Austin", "TX", "78701", "US");
        var withoutSuite = Address.FromParts("1600 Main St", "  ", "Austin", "TX", "78701", "US");

        Assert.Equal("Suite 200", withSuite!.Line2);
        Assert.Null(withoutSuite!.Line2);
    }

    [Fact]
    public void No_parts_at_all_means_no_address()
    {
        Assert.Null(Address.FromParts(null, "", " ", null, null, "US"));
    }

    [Theory]
    [InlineData("usa")]
    [InlineData("United States")]
    [InlineData("")]
    [InlineData(null)]
    public void Common_spellings_of_the_us_are_stored_as_us(string? country)
    {
        Assert.Equal("US", Address.FromParts("1 Main", null, "Austin", "TX", "78701", country)!.Country);
    }

    [Fact]
    public void Other_countries_are_upper_cased()
    {
        var address = Address.FromParts("10 Downing St", null, "London", "Greater London", "SW1A 2AA", "gb");

        Assert.Equal("GB", address!.Country);
        Assert.Equal("SW1A 2AA", address.Zip);
    }

    [Fact]
    public void Single_line_joins_only_the_parts_present()
    {
        Assert.Equal(
            "1600 Main St, Suite 200, Austin, TX 78701, US",
            new Address("1600 Main St", "Suite 200", "Austin", "TX", "78701", "US").ToSingleLine()
        );
        Assert.Equal("TX 78701, US", new Address("", null, "", "TX", "78701", "US").ToSingleLine());
    }

    [Fact]
    public void Single_line_is_null_for_an_empty_or_legacy_placeholder_address()
    {
        Assert.Null(Address.Empty().ToSingleLine());
        // Las facturas de antes de F0 guardaban un guion largo como calle cuando el emisor no tenía dirección.
        Assert.Null(new Address("—", null, "", "", "", "US").ToSingleLine());
    }
}
