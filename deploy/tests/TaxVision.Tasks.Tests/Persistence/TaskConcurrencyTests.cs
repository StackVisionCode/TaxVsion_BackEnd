using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Va contra SQL Server real porque el proveedor InMemory no materializa <c>rowversion</c>: el mismo
/// test pasaría con <c>IsRowVersion()</c> borrado del config. Construye el <c>DbContext</c> con
/// <c>UseSqlServer</c> directo, sin levantar el host, para no arrastrar RabbitMQ ni Redis.
///
/// <para>
/// La excepción esperada es <see cref="DbUpdateConcurrencyException"/>, no <c>ConflictException</c>:
/// el <c>DbContext</c> sólo traduce <c>SqlException</c> 2601/2627, y un choque de <c>rowversion</c>
/// ni siquiera llega a SQL Server — EF ve 0 filas afectadas y lanza la suya.
/// </para>
/// </summary>
public sealed class TaskConcurrencyTests
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
    public async Task Two_concurrent_completions_of_the_same_task_lose_the_race()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var task = NewTask(tenantId, userId, nowUtc);
        await using (var seed = CreateContext())
        {
            seed.Tasks.Add(task);
            await seed.SaveChangesAsync();
        }

        // Dos contextos con la misma versión de la fila: el usuario completando y el handler de
        // desbloqueo tocando el mismo agregado.
        await using var first = CreateContext();
        await using var second = CreateContext();

        var fromFirst = await LoadAsync(first, tenantId, task.Id);
        var fromSecond = await LoadAsync(second, tenantId, task.Id);

        fromFirst.Complete(userId, nowUtc);
        await first.SaveChangesAsync();

        fromSecond.Complete(userId, nowUtc.AddSeconds(1));
        var loser = await Record.ExceptionAsync(() => second.SaveChangesAsync());

        Assert.IsType<DbUpdateConcurrencyException>(loser);

        await using var verify = CreateContext();
        var persisted = await LoadAsync(verify, tenantId, task.Id);
        Assert.Equal(TaskItemStatus.Completed, persisted.Status);
    }

    /// <summary>Si el <c>RowVersion</c> no cambiara en cada escritura, el token no protegería nada.</summary>
    [Fact]
    public async Task RowVersion_changes_on_every_write()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var task = NewTask(tenantId, userId, nowUtc);
        await using var db = CreateContext();
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var afterInsert = task.RowVersion.ToArray();
        Assert.NotEmpty(afterInsert);

        task.Start(userId, nowUtc);
        await db.SaveChangesAsync();

        Assert.NotEqual(afterInsert, task.RowVersion);
    }

    private static async Task<TaskItem> LoadAsync(TasksDbContext db, Guid tenantId, Guid taskId) =>
        await db.Tasks.IgnoreQueryFilters().SingleAsync(t => t.TenantId == tenantId && t.Id == taskId);

    private static TaskItem NewTask(Guid tenantId, Guid userId, DateTime nowUtc) =>
        TaskItem
            .Create(
                tenantId,
                userId,
                TaskTitle.Create("Concurrency probe").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                userId,
                nowUtc
            )
            .Value;
}
