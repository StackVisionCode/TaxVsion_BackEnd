using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.Domain.RateLimiting;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsTenantDirectoryTests
{
    [Fact]
    public async Task Lists_only_tenants_whose_plan_projection_has_planner()
    {
        await using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var withPlanner = Projection(Guid.NewGuid(), "Planner", "wallet");
        var withoutPlanner = Projection(Guid.NewGuid(), "wallet");
        db.TenantPlanCodeProjections.AddRange(withPlanner, withoutPlanner);
        await db.SaveChangesAsync();

        // La implementación es interna; se instancia por su contrato.
        var directory = (IClientFormsTenantDirectory)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.ClientFormsTenantDirectory",
                    throwOnError: true
                )!,
                db
            )!;

        Assert.Equal([withPlanner.TenantId], await directory.ListAvailableTenantIdsAsync());
    }

    private static TenantPlanCodeProjection Projection(Guid tenantId, params string[] modules)
    {
        var projection = TenantPlanCodeProjection.Create(tenantId, "pro", 1);
        projection.ApplyIfNewer("pro", 1, modules);
        return projection;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
