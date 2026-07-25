using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (RabbitMQ/JWT) al crear o aplicar migraciones. La cadena de conexión se toma,
/// en orden, de: --connection, la variable ConnectionStrings__Default, o un
/// fallback local de desarrollo.
/// </summary>
public sealed class SubscriptionDbContextFactory : IDesignTimeDbContextFactory<SubscriptionDbContext>
{
    public SubscriptionDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVisionSubscription;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(connectionString).Options;

        // dotnet-ef solo inspecciona el modelo, nunca ejecuta una query real.
        return new SubscriptionDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
