using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Tenant.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (JWT/RabbitMQ/user-secrets) al crear o aplicar migraciones. La cadena de
/// conexión se toma de --connection, de la variable ConnectionStrings__Default,
/// o de un fallback local de desarrollo.
/// </summary>
public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Tenants;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(connectionString).Options;

        return new TenantDbContext(options, new EmptyTenantContext());
    }

    /// <summary>H-11 — el filtro global fail-closed necesita un ITenantContext; en tiempo de diseño
    /// no hay request ni tenant, y `HasTenant = false` hace que el filtro compare contra Guid.Empty
    /// (irrelevante para generar el modelo). Mismo patrón que BillingDbContextFactory.</summary>
    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set at design time.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) =>
            throw new InvalidOperationException("TenantId cannot be set at design time.");
    }
}
