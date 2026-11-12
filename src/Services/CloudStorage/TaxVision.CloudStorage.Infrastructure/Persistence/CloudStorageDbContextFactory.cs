using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TaxVision.CloudStorage.Infrastructure.Persistence;

public sealed class CloudStorageDbContextFactory : IDesignTimeDbContextFactory<CloudStorageDbContext>
{
    public CloudStorageDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<CloudStorageDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString =
            Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "CloudStorage design-time connection is missing. Set ConnectionStrings:Default in User Secrets "
                    + "or CLOUDSTORAGE_DB_CONNECTION in the environment."
            );
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
