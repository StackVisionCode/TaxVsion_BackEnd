extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Scribe.Infrastructure;
using TaxVision.Scribe.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Scribe.Tests.Architecture;

/// <summary>Scribe conoce la zona de cada oficina por la proyección compartida.</summary>
public sealed class TenantTimeZoneAdoptionTests
{
    private const string ApiProject = "src/Services/Scribe/TaxVision.Scribe.Api";

    [Fact]
    public void The_db_context_maps_the_office_time_zone_projection()
    {
        using var db = new ScribeDbContext(
            new DbContextOptionsBuilder<ScribeDbContext>()
                .UseSqlServer("Server=model-only;Database=model-only")
                .Options,
            new NoTenant()
        );

        Assert.NotNull(db.Model.FindEntityType(typeof(TenantTimeZoneProjection)));
    }

    [Theory]
    [InlineData("TenantCreatedIntegrationEventHandler")]
    [InlineData("TenantUpdatedIntegrationEventHandler")]
    public void Tenant_events_feed_the_shared_projection_consumer(string handlerPrefix)
    {
        var file = Directory
            .GetFiles(
                PreGeneratedCodeRules.GeneratedDirectory(ApiProject),
                handlerPrefix + "*.cs",
                SearchOption.AllDirectories
            )
            .Single();

        Assert.Contains(
            "BuildingBlocks.TenantTimeZones.TenantTimeZoneProjectionConsumer.Handle",
            File.ReadAllText(file)
        );
    }

    [Fact]
    public void Infrastructure_registers_the_resolver_and_the_reconciliation_against_tenant()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=model-only;Database=model-only",
                }
            )
            .Build();
        var services = new ServiceCollection();

        services.AddScribeInfrastructure(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(ITenantTimeZoneResolver));
        Assert.Contains(services, d => d.ServiceType == typeof(ITenantTimeZoneTokenProvider));
        Assert.Contains(services, d => d.ImplementationType == typeof(TenantTimeZoneReconciliationJob));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
