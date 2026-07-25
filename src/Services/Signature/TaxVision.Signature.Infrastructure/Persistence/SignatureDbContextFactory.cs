using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Signature.Infrastructure.Persistence;

/// <summary>
/// Design-time factory usada por <c>dotnet ef</c> para crear migrations sin arrancar
/// el host. Toma la connection string de la variable de entorno cuando existe; caso
/// contrario, apunta a un SQL Server local para desarrollo. Usa un tenant context
/// vacío (<c>HasTenant = false</c>) — no hay filtro activo en design time.
/// </summary>
public sealed class SignatureDbContextFactory : IDesignTimeDbContextFactory<SignatureDbContext>
{
    public SignatureDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Signature;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer(connectionString).Options;

        return new SignatureDbContext(options, new EmptyTenantContext());
    }

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("No tenant set in design-time context.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
