using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Campaigns.Tests.Architecture;

/// <summary>Índices de la lista de runs recientes del dashboard (sin recorrer la oficina entera).</summary>
public sealed class CampaignRunIndexTests
{
    [Fact]
    public void The_runs_table_has_a_tenant_recent_index()
    {
        var index = FindIndex(typeof(CampaignRun), "IX_CampaignRuns_TenantId_CreatedAtUtc");

        Assert.Equal(
            [nameof(CampaignRun.TenantId), nameof(CampaignRun.CreatedAtUtc)],
            index.Properties.Select(p => p.Name)
        );
        Assert.Equal([false, true], index.IsDescending);
    }

    [Fact]
    public void The_schedules_table_has_a_tenant_next_fire_index()
    {
        var index = FindIndex(typeof(CampaignSchedule), "IX_CampaignSchedules_TenantId_Status_NextFireAtUtc");

        Assert.Equal(
            [
                nameof(CampaignSchedule.TenantId),
                nameof(CampaignSchedule.Status),
                nameof(CampaignSchedule.NextFireAtUtc),
            ],
            index.Properties.Select(p => p.Name)
        );
    }

    private static IIndex FindIndex(Type entity, string name)
    {
        using var db = new CampaignsDbContext(
            new DbContextOptionsBuilder<CampaignsDbContext>()
                .UseSqlServer("Server=model-only;Database=model-only")
                .Options,
            new NoTenant()
        );
        // Modelo de diseño: el de ejecución no guarda IsDescending.
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
