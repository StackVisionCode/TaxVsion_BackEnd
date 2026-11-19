using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TaxVision.Mobile.Infrastructure.Persistence;

public sealed class MobileDbContextFactory : IDesignTimeDbContextFactory<MobileDbContext>
{
    public MobileDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("MOBILE_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("taxvision-mobile-development")
                .Build()
                .GetConnectionString("Default")
            ?? "Server=localhost,1433;Database=TaxVision_Mobile;Trusted_Connection=True;TrustServerCertificate=True";
        return new MobileDbContext(
            new DbContextOptionsBuilder<MobileDbContext>().UseSqlServer(connection).Options,
            new DesignTimeTenant()
        );
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
