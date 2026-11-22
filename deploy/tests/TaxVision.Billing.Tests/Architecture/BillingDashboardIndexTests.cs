using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

/// <summary>Índices del resumen del dashboard medidos con volumen: aging sin lookups y "mis clientes" por usuario.</summary>
public sealed class BillingDashboardIndexTests
{
    [Fact]
    public void The_aging_index_includes_the_text_column_behind_the_computed_cents()
    {
        // SQL Server expande AmountDueCents a la columna AmountDue dentro de SUM(CASE ...): sin ella hace un lookup por fila.
        var index = FindIndex(typeof(Invoice), "IX_Invoices_TenantId_Status");

        Assert.Contains(nameof(Invoice.AmountDue), index.GetIncludeProperties()!);
    }

    [Fact]
    public void The_assignment_projection_has_a_per_user_index()
    {
        var index = FindIndex(typeof(CustomerAssignmentProjection), "IX_CustomerAssignmentProjections_TenantId_UserId");

        Assert.Equal(
            [nameof(CustomerAssignmentProjection.TenantId), nameof(CustomerAssignmentProjection.UserId)],
            index.Properties.Select(p => p.Name)
        );
        Assert.Equal([nameof(CustomerAssignmentProjection.CustomerId)], index.GetIncludeProperties()!);
    }

    private static IIndex FindIndex(Type entity, string name)
    {
        using var db = new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>()
                .UseSqlServer("Server=model-only;Database=model-only")
                .Options,
            new NoTenant()
        );
        var index = db.GetService<IDesignTimeModel>()
            .Model.FindEntityType(entity)!
            .GetIndexes()
            .SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        return index;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
