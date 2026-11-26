using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Ubicación aproximada de una sesión: país obligatorio, coordenadas solo si son creíbles.</summary>
public sealed class SessionLocationTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("U")]
    [InlineData("USA")]
    [InlineData("1A")]
    public void Without_a_two_letter_country_there_is_no_location(string? country)
    {
        Assert.True(SessionLocation.Create(country, "California", "Mountain View", 37.42, -122.08, Now).IsFailure);
    }

    [Fact]
    public void The_country_is_kept_in_upper_case()
    {
        Assert.Equal("US", SessionLocation.Create("us", null, null, null, null, Now).Value.CountryCode);
    }

    [Fact]
    public void Coordinates_are_rounded_to_two_decimals()
    {
        var location = SessionLocation.Create("US", "California", "Mountain View", 37.4221, -122.0849, Now).Value;

        Assert.Equal(37.42, location.Latitude);
        Assert.Equal(-122.08, location.Longitude);
        Assert.Equal("DB-IP", location.Source);
        Assert.Equal(Now, location.ResolvedAtUtc);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(91d, 10d)]
    [InlineData(10d, -181d)]
    [InlineData(null, 10d)]
    [InlineData(10d, null)]
    public void Zero_zero_out_of_range_or_half_coordinates_are_dropped(double? latitude, double? longitude)
    {
        var location = SessionLocation.Create("DO", null, "Santo Domingo", latitude, longitude, Now).Value;

        Assert.Null(location.Latitude);
        Assert.Null(location.Longitude);
        Assert.Equal("Santo Domingo", location.City);
    }

    [Fact]
    public void Long_names_are_cut_and_blank_ones_dropped()
    {
        var location = SessionLocation.Create("US", new string('r', 300), "  ", null, null, Now).Value;

        Assert.Equal(128, location.Region!.Length);
        Assert.Null(location.City);
    }
}
