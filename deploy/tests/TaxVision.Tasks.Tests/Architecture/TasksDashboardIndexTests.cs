using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.Architecture;

/// <summary>T-1: el recorte de tareas lee los clientes del empleado por usuario, sin recorrer el tenant.</summary>
public sealed class TasksDashboardIndexTests
{
    [Fact]
    public void The_assignment_projection_has_a_per_user_index()
    {
        using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer("Server=model-only;Database=model-only").Options,
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
