using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Auth.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (JWT/RabbitMQ/user-secrets) al crear o aplicar migraciones. La cadena de
/// conexión se toma de --connection, de la variable ConnectionStrings__Default,
/// o de un fallback local de desarrollo.
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Auth;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(connectionString).Options;

        // dotnet-ef solo inspecciona el modelo (OnModelCreating) — nunca ejecuta una query real, así
        // que no hace falta un ITenantContext funcional (HasQueryFilter solo arma la expression tree
        // en tiempo de modelado; nunca se evalúa HasTenant/TenantId).
        return new AuthDbContext(options, new DesignTimeOnlyTenantContext());
    }

    /// <summary>No-op: dotnet-ef nunca ejecuta una query, así que el filtro nunca evalúa estas propiedades.</summary>
    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
