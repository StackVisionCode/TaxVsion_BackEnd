using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Tests.Architecture;

/// <summary>T-1: el recorte del resumen parte de los clientes del empleado, por usuario y sin recorrer el tenant.</summary>
public sealed class SignatureDashboardIndexTests
{
    [Fact]
    public void The_assignment_projection_has_a_per_user_index()
    {
        using var db = new SignatureDbContext(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer("Server=model-only;Database=model-only")
                .Options,
            new NoTenant()
        );

        var index = db.GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(CustomerAssignmentProjection))!
            .GetIndexes()
            .SingleOrDefault(i => i.GetDatabaseName() == "IX_CustomerAssignmentProjections_TenantId_UserId");

        Assert.NotNull(index);
        Assert.Equal(
            [nameof(CustomerAssignmentProjection.TenantId), nameof(CustomerAssignmentProjection.UserId)],
            index.Properties.Select(p => p.Name)
        );
        Assert.Equal([nameof(CustomerAssignmentProjection.CustomerId)], index.GetIncludeProperties()!);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
