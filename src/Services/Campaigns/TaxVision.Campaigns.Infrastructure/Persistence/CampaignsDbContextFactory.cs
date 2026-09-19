using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Campaigns.Infrastructure.Persistence;

/// <summary>Design-time factory para <c>dotnet ef</c> — evita levantar RabbitMQ/JWT solo para migrar.</summary>
public sealed class CampaignsDbContextFactory : IDesignTimeDbContextFactory<CampaignsDbContext>
{
    public CampaignsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Campaigns;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CampaignsDbContext>().UseSqlServer(connectionString).Options;

        return new CampaignsDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
