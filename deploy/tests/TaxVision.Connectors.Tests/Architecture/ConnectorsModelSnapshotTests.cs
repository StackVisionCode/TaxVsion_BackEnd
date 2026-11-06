using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Infrastructure.Persistence;

namespace TaxVision.Connectors.Tests.Architecture;

// Pasar TenantEmailAccount a raíz con eventos no cambia el esquema: el modelo sigue igual a la última migración.
public sealed class ConnectorsModelSnapshotTests
{
    [Fact]
    public void Model_MatchesTheLastMigration()
    {
        using var db = new ConnectorsDbContext(
            new DbContextOptionsBuilder<ConnectorsDbContext>()
                .UseSqlServer("Server=127.0.0.1,1;Database=unused")
                .Options,
            new NoTenant()
        );

        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
