using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Mobile.Infrastructure.Persistence;

/// <summary>
/// Factory de diseño para <c>dotnet ef</c>. Mismo orden que Notes: <c>ConnectionStrings__Default</c> (lo fija
/// <c>migrate-local.ps1</c> solo para su proceso hijo) o la base local. No lee <c>MOBILE_DB_CONNECTION</c>: es
/// el nombre del <c>.env</c> de producción y, cargado en la shell, mandaría <c>dotnet ef</c> a producción.
/// </summary>
public sealed class MobileDbContextFactory : IDesignTimeDbContextFactory<MobileDbContext>
{
    public const string LocalConnection =
        "Server=localhost,1433;Database=TaxVision_Mobile;Trusted_Connection=True;TrustServerCertificate=True";

    public MobileDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? LocalConnection;
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
