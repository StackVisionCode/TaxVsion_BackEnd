using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence;

public sealed class FinancialInsightsDbContextFactory : IDesignTimeDbContextFactory<FinancialInsightsDbContext>
{
    public FinancialInsightsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("FINANCIAL_INSIGHTS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("taxvision-financialinsights-development")
                .Build()
                .GetConnectionString("Default")
            ?? "Server=localhost,1433;Database=TaxVision_FinancialInsights;Trusted_Connection=True;TrustServerCertificate=True";
        return new FinancialInsightsDbContext(new DbContextOptionsBuilder<FinancialInsightsDbContext>().UseSqlServer(connection).Options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;
        public void SetTenant(Guid tenantId) { }
    }
}
