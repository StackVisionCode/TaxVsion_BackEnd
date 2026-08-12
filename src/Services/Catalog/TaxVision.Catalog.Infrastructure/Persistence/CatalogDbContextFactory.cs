using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Catalog.Infrastructure.Persistence;

/// <summary>Design-time factory para <c>dotnet ef</c> — evita levantar RabbitMQ/JWT solo para migrar.</summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Catalog;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseSqlServer(connectionString).Options;
        return new CatalogDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
