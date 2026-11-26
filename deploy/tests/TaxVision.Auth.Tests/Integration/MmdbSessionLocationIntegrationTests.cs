using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Infrastructure.Security;
using Xunit.Abstractions;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Con el archivo real de DB-IP (scripts/geoip/download-geoip-database.ps1). Si el archivo no está en esta
/// máquina, las pruebas no hacen nada: el archivo no se versiona.
/// </summary>
public sealed class MmdbSessionLocationIntegrationTests(ITestOutputHelper output)
{
    private static readonly string DatabasePath =
        Environment.GetEnvironmentVariable("AUTH_GEOIP_DATABASE")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaxVision",
            "geoip",
            "dbip-city-lite.mmdb"
        );

    private static MmdbSessionLocationResolver Resolver() =>
        new(
            Options.Create(new SessionLocationOptions { DatabasePath = DatabasePath }),
            NullLogger<MmdbSessionLocationResolver>.Instance
        );

    [Fact]
    public void A_public_ipv4_resolves_to_a_country_city_and_coordinates()
    {
        if (!File.Exists(DatabasePath))
            return;
        using var resolver = Resolver();

        var location = resolver.Resolve("8.8.8.8");

        Assert.NotNull(location);
        Assert.Equal(2, location.CountryCode.Length);
        Assert.NotNull(location.City);
        Assert.NotNull(location.Latitude);
        Assert.NotNull(location.Longitude);
    }

    [Fact]
    public void A_public_ipv6_and_a_mapped_ipv4_do_not_throw()
    {
        if (!File.Exists(DatabasePath))
            return;
        using var resolver = Resolver();

        var v6 = resolver.Resolve("2001:4860:4860::8888");
        var mapped = resolver.Resolve("::ffff:8.8.8.8");

        Assert.Equal(2, v6!.CountryCode.Length);
        Assert.Equal(resolver.Resolve("8.8.8.8")!.CountryCode, mapped!.CountryCode);
    }

    /// <summary>Medición de la fase: 10 000 consultas, media por debajo de 1 ms y menos de 150 MB de memoria privada.</summary>
    [Fact]
    public void Ten_thousand_lookups_stay_fast_and_light()
    {
        if (!File.Exists(DatabasePath))
            return;
        using var resolver = Resolver();
        resolver.Resolve("8.8.8.8");
        var random = new Random(20261125);
        var addresses = Enumerable
            .Range(0, 10_000)
            .Select(_ => $"{random.Next(1, 223)}.{random.Next(0, 256)}.{random.Next(0, 256)}.{random.Next(1, 255)}")
            .ToArray();

        GC.Collect();
        var before = Process.GetCurrentProcess().PrivateMemorySize64;
        var watch = Stopwatch.StartNew();
        foreach (var address in addresses)
            resolver.Resolve(address);
        watch.Stop();
        var after = Process.GetCurrentProcess().PrivateMemorySize64;

        var averageMs = watch.Elapsed.TotalMilliseconds / addresses.Length;
        var growthMb = (after - before) / (1024d * 1024d);
        output.WriteLine($"average {averageMs:F4} ms, private memory +{growthMb:F1} MB");
        Assert.True(averageMs < 1, $"average {averageMs:F4} ms");
        Assert.True(growthMb < 150, $"private memory +{growthMb:F1} MB");
    }
}
