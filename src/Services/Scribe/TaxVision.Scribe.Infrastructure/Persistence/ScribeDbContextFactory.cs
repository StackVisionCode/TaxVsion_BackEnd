using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Scribe.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo (RabbitMQ/JWT) al
/// crear o aplicar migraciones. La cadena de conexión se toma de ConnectionStrings__Default o un
/// fallback local de desarrollo.
/// </summary>
public sealed class ScribeDbContextFactory : IDesignTimeDbContextFactory<ScribeDbContext>
{
    public ScribeDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Scribe;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ScribeDbContext>().UseSqlServer(connectionString).Options;

        // dotnet-ef solo inspecciona el modelo, nunca ejecuta una query real.
        return new ScribeDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
