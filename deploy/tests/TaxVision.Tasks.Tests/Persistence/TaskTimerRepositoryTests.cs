using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// El reporte agrupa y suma en SQL sobre <c>DATEDIFF</c>. InMemory lo resolvería en memoria y no
/// probaría la traducción, que es justo lo que puede romperse.
/// </summary>
public sealed class TaskTimerRepositoryTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Tasks;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly DateTime Now = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

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
    public async Task The_report_sums_the_closed_stretches_of_each_person()
    {
        var tenantId = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();

        var task = NewTask(tenantId, ana);
        StopAfter(task, ana, minutes: 90);
        StopAfter(task, ana, minutes: 30);
        StopAfter(task, beto, minutes: 60);

        await SeedAsync(task);

        await using var db = CreateContext();
        var rows = await new TaskTimerRepository(db).ListReportAsync(tenantId, Now.AddDays(-1), Now.AddDays(1), null);

        Assert.Equal(2m, rows.Single(r => r.UserId == ana).Hours);
        Assert.Equal(2, rows.Single(r => r.UserId == ana).Entries);
        Assert.Equal(1m, rows.Single(r => r.UserId == beto).Hours);
    }

    /// <summary>Un tramo abierto no se imputa: las horas se cuentan al parar el reloj.</summary>
    [Fact]
    public async Task A_running_timer_does_not_enter_the_report()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var task = NewTask(tenantId, userId);
        task.StartTimer(userId, isBillable: true, Now);

        await SeedAsync(task);

        await using var db = CreateContext();
        var rows = await new TaskTimerRepository(db).ListReportAsync(tenantId, Now.AddDays(-1), Now.AddDays(1), null);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task The_report_can_be_narrowed_to_one_person()
    {
        var tenantId = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();

        var task = NewTask(tenantId, ana);
        StopAfter(task, ana, minutes: 60);
        StopAfter(task, beto, minutes: 60);

        await SeedAsync(task);

        await using var db = CreateContext();
        var rows = await new TaskTimerRepository(db).ListReportAsync(tenantId, Now.AddDays(-1), Now.AddDays(1), ana);

        Assert.Equal([ana], rows.Select(r => r.UserId).ToArray());
    }

    private static void StopAfter(TaskItem task, Guid userId, int minutes)
    {
        var timer = task.StartTimer(userId, isBillable: true, Now).Value;
        task.StopTimer(timer.Id, userId, Now.AddMinutes(minutes));
    }

    private static async Task SeedAsync(params TaskItem[] tasks)
    {
        await using var db = CreateContext();
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync();
    }

    private static TaskItem NewTask(Guid tenantId, Guid assigneeUserId) =>
        TaskItem
            .Create(
                tenantId,
                assigneeUserId,
                TaskTitle.Create("Timer probe").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                assigneeUserId,
                Now
            )
            .Value;
}
