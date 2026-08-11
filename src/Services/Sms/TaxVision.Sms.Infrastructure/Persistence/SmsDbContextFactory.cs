using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Sms.Infrastructure.Persistence;

/// <summary>Design-time factory para <c>dotnet ef</c> — evita levantar RabbitMQ/JWT solo para migrar.</summary>
public sealed class SmsDbContextFactory : IDesignTimeDbContextFactory<SmsDbContext>
{
    public SmsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Sms;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SmsDbContext>().UseSqlServer(connectionString).Options;
        return new SmsDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
