using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.TimeZones;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>El almacén real del cambio de zona: carga las series vivas (activas o pausadas) de las zonas pedidas.</summary>
public sealed class CivilDueRezoneStoreTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Tasks;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static TasksDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    [Fact]
    public async Task Paused_and_fallback_series_are_loaded_but_ended_and_other_zones_are_not()
    {
        var tenant = Guid.NewGuid();
        var active = NewSeries(tenant, "America/New_York");
        var paused = NewSeries(tenant, "America/New_York");
        paused.Pause();
        var fallback = NewSeries(tenant, IanaTimeZone.UtcId);
        var ended = NewSeries(tenant, "America/New_York");
        ended.End();
        var elsewhere = NewSeries(tenant, "America/Denver");
        await using (var seed = CreateContext())
        {
            seed.TaskSeries.AddRange(active, paused, fallback, ended, elsewhere);
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var store = (ICivilDueRezoneStore)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.Persistence.Repositories.CivilDueRezoneStore",
                    true
                )!,
                db
            )!;

        var loaded = await store.SeriesToRezoneAsync(
            tenant,
            ["America/New_York", IanaTimeZone.UtcId],
            CancellationToken.None
        );

        Assert.Equal(new[] { active.Id, paused.Id, fallback.Id }.Order(), loaded.Select(s => s.Id).Order());
    }

    private static TaskSeries NewSeries(Guid tenant, string zone) =>
        TaskSeries
            .Create(
                tenant,
                Guid.NewGuid(),
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=20", zone).Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Monthly close").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Guid.NewGuid(),
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(10),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
}
