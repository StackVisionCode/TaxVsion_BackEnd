using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Growth.Infrastructure;
using TaxVision.Growth.Infrastructure.Persistence;

namespace TaxVision.Growth.Tests.Infrastructure;

/// <summary>
/// El ejecutor de idempotencia de Growth necesita savepoints dentro de la transacción del handler, y EF los apaga
/// cuando la conexión tiene MARS. La cadena de producción lo traía: regenerar el código de referido daba 503
/// (<c>Growth.Idempotency.SavepointsRequired</c>). El contexto se registra siempre sin MARS.
/// </summary>
public sealed class GrowthDbConnectionTests
{
    [Fact]
    public void The_growth_context_never_uses_mars_even_if_the_connection_string_asks_for_it()
    {
        using var provider = Services(
            "Server=localhost,1433;Database=TaxVision_Growth;User Id=x;Password=y;MultipleActiveResultSets=True"
        );
        using var scope = provider.CreateScope();

        var connection = scope.ServiceProvider.GetRequiredService<GrowthDbContext>().Database.GetConnectionString();

        Assert.False(new SqlConnectionStringBuilder(connection).MultipleActiveResultSets);
        Assert.Equal("TaxVision_Growth", new SqlConnectionStringBuilder(connection).InitialCatalog);
    }

    private static ServiceProvider Services(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrowthInfrastructure(configuration);
        services.AddScoped<ITenantContext>(_ => new NoTenant());
        return services.BuildServiceProvider();
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
