using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.CloudStorage.Infrastructure.Persistence;

public sealed class CloudStorageDbContextFactory : IDesignTimeDbContextFactory<CloudStorageDbContext>
{
    public CloudStorageDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
            ?? "Server=localhost;Database=TaxVision_CloudStorage;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(connectionString).Options;
        // dotnet-ef solo inspecciona el modelo, nunca ejecuta una query real.
        return new CloudStorageDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
