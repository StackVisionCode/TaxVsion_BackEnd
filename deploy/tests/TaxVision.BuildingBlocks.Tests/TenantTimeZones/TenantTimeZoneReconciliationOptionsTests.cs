using BuildingBlocks.TenantTimeZones;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TenantTimeZones;

/// <summary>La URL de Tenant sale de la sección propia, si no de PlatformSettings (ya está en el compose), si no la local.</summary>
public sealed class TenantTimeZoneReconciliationOptionsTests
{
    [Fact]
    public void Own_section_wins()
    {
        var options = Resolve(
            new()
            {
                ["TenantTimeZones:Reconciliation:TenantBaseUrl"] = "http://own:8080",
                ["PlatformSettings:TenantBaseUrl"] = "http://tenant-api:8080",
            }
        );

        Assert.Equal("http://own:8080", options.TenantBaseUrl);
    }

    [Fact]
    public void Falls_back_to_the_platform_settings_tenant_url()
    {
        var options = Resolve(new() { ["PlatformSettings:TenantBaseUrl"] = "http://tenant-api:8080" });

        Assert.Equal("http://tenant-api:8080", options.TenantBaseUrl);
    }

    [Fact]
    public void Without_configuration_uses_the_local_fleet_port()
    {
        Assert.Equal("http://localhost:5217", Resolve(new()).TenantBaseUrl);
    }

    private static TenantTimeZoneReconciliationOptions Resolve(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenantTimeZoneReconciliation<NoToken>(configuration);
        return services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<TenantTimeZoneReconciliationOptions>>()
            .Value;
    }

    private sealed class NoToken : ITenantTimeZoneTokenProvider
    {
        public Task<string?> GetServiceTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
}
