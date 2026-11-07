using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Tests.Architecture;

// [NotMapped] pasó a builder.Ignore: el modelo sigue igual a la última migración.
public sealed class SignatureModelSnapshotTests
{
    [Fact]
    public void Model_MatchesTheLastMigration()
    {
        using var db = new SignatureDbContext(
            new DbContextOptionsBuilder<SignatureDbContext>()
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
