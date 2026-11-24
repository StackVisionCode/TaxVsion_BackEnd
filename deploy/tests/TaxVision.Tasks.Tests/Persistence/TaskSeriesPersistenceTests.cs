using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Editar una serie reemplaza su molde (un owned con otro owned dentro). Contra SQL Server real: EF no admite que el
/// molde nuevo comparta la referencia anidada con el anterior.
/// </summary>
public sealed class TaskSeriesPersistenceTests
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
    public async Task Revising_the_blueprint_and_the_schedule_is_saved_and_keeps_the_reference()
    {
        var tenantId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var series = await SeedAsync(tenantId, owner, customerId);

        await using (var edit = CreateContext())
        {
            var loaded = (await new TaskSeriesRepository(edit).GetByIdAsync(tenantId, series.Id)).Value;
            var revised = loaded.Revise(
                loaded.Blueprint with
                {
                    Title = TaskTitle.Create("Payroll").Value,
                    Priority = TaskPriority.High,
                },
                null,
                null
            );
            var rescheduled = loaded.Reschedule(
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=-1", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                DateTime.UtcNow.AddDays(40)
            );
            await edit.SaveChangesAsync();

            Assert.True(revised.IsSuccess && rescheduled.IsSuccess);
            Assert.NotNull(loaded.Blueprint.Reference);
        }

        await using var read = CreateContext();
        var stored = (await new TaskSeriesRepository(read).GetByIdAsync(tenantId, series.Id)).Value;
        Assert.Equal("Payroll", stored.Blueprint.Title.Value);
        Assert.Equal(customerId, stored.Blueprint.Reference.CustomerId);
        Assert.Equal("FREQ=MONTHLY;BYMONTHDAY=-1", stored.Rule.Value);
    }

    [Fact]
    public async Task Reassigning_the_blueprint_on_offboarding_keeps_the_client_and_tax_year()
    {
        var tenantId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var series = await SeedAsync(tenantId, owner, customerId);

        await using (var edit = CreateContext())
        {
            var loaded = (await new TaskSeriesRepository(edit).GetByIdAsync(tenantId, series.Id)).Value;
            Assert.True(loaded.ReassignBlueprintAssignee(successor).IsSuccess);
            await edit.SaveChangesAsync();

            Assert.NotNull(loaded.Blueprint.Reference);
        }

        await using var read = CreateContext();
        var stored = (await new TaskSeriesRepository(read).GetByIdAsync(tenantId, series.Id)).Value;
        Assert.Equal(successor, stored.Blueprint.AssigneeUserId);
        Assert.Equal((customerId, 2026), (stored.Blueprint.Reference.CustomerId, stored.Blueprint.Reference.TaxYear));
    }

    private static async Task<TaskSeries> SeedAsync(Guid tenantId, Guid owner, Guid customerId)
    {
        var series = TaskSeries
            .Create(
                tenantId,
                owner,
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=15", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Monthly bookkeeping").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.Create(customerId, 2026).Value,
                    AssigneeUserId = owner,
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(30),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        await using (var seed = CreateContext())
        {
            seed.TaskSeries.Add(series);
            await seed.SaveChangesAsync();
        }
        return series;
    }
}
