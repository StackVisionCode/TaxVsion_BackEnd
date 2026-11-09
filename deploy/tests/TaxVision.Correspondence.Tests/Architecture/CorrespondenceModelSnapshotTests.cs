using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Architecture;

// Pasar EmailThread e IncomingEmail a raíz con eventos no cambia el esquema: el modelo sigue igual a la última migración.
public sealed class CorrespondenceModelSnapshotTests
{
    [Fact]
    public void Model_MatchesTheLastMigration()
    {
        using var db = new CorrespondenceDbContext(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
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
