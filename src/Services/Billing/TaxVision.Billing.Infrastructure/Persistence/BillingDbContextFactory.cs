using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Billing.Infrastructure.Persistence;

/// <summary>Construcción en tiempo de diseño (migraciones EF) sin arrancar RabbitMQ ni el host
/// HTTP. No crea ni aplica migraciones.</summary>
public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Billing;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(connectionString).Options;
        return new BillingDbContext(options, new EmptyTenantContext(), messageBus: null);
    }

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set at design time.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) =>
            throw new InvalidOperationException("TenantId cannot be set at design time.");
    }
}
