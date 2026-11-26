using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>El resolvedor nunca rompe el inicio de sesión: sin archivo o con una IP que no aplica, no hay ubicación.</summary>
public sealed class MmdbSessionLocationResolverTests
{
    private static MmdbSessionLocationResolver Resolver(string? path) =>
        new(
            Options.Create(new SessionLocationOptions { DatabasePath = path }),
            NullLogger<MmdbSessionLocationResolver>.Instance
        );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:/no/such/folder/dbip-city-lite.mmdb")]
    public void Without_the_file_there_is_no_location_and_nothing_throws(string? path)
    {
        using var resolver = Resolver(path);

        Assert.Null(resolver.Resolve("8.8.8.8"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.9")]
    [InlineData("172.31.255.1")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.10.1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.20")]
    public void Local_private_or_unreadable_addresses_are_not_looked_up(string? ip)
    {
        Assert.Null(MmdbSessionLocationResolver.Lookupable(ip));
    }

    [Fact]
    public void An_ipv4_written_as_ipv6_is_looked_up_as_ipv4()
    {
        Assert.Equal("8.8.8.8", MmdbSessionLocationResolver.Lookupable("::ffff:8.8.8.8")!.ToString());
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:4860:4860::8888")]
    public void Public_addresses_are_looked_up(string ip)
    {
        Assert.NotNull(MmdbSessionLocationResolver.Lookupable(ip));
    }
}
