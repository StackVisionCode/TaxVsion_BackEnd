using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaxVision.Documents.Infrastructure.Persistence;

/// <summary>Construcción en tiempo de diseño (migraciones EF) sin RabbitMQ ni host HTTP.</summary>
public sealed class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Documents;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(connectionString).Options;
        return new DocumentsDbContext(options, new EmptyTenantContext(), messageBus: null);
    }

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set at design time.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) =>
            throw new InvalidOperationException("TenantId cannot be set at design time.");
    }
}
