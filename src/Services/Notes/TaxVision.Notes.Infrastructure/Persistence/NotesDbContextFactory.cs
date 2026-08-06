using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Notes.Infrastructure.Persistence;

/// <summary>Design-time factory para <c>dotnet ef</c> — evita levantar RabbitMQ/JWT solo para migrar.</summary>
public sealed class NotesDbContextFactory : IDesignTimeDbContextFactory<NotesDbContext>
{
    public NotesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Notes;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(connectionString).Options;

        // dotnet-ef solo inspecciona el modelo, nunca ejecuta una query real.
        return new NotesDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
