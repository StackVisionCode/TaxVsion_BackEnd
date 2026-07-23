using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Growth.Infrastructure.Persistence;

/// <summary>
/// Design-time construction without starting RabbitMQ or the HTTP host. This factory does
/// not create or apply migrations.
/// </summary>
public sealed class GrowthDbContextFactory : IDesignTimeDbContextFactory<GrowthDbContext>
{
    public GrowthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Growth;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<GrowthDbContext>().UseSqlServer(connectionString).Options;
        return new GrowthDbContext(options, new EmptyTenantContext(), messageBus: null);
    }

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set at design time.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
