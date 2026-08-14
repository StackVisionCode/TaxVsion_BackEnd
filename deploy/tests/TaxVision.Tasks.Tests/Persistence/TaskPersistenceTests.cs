using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Lo que InMemory sí puede cubrir: round-trip de los 6 VOs y aislamiento entre tenants. La
/// concurrencia va aparte, en <see cref="TaskConcurrencyTests"/>, contra SQL Server real.
/// </summary>
public sealed class TaskPersistenceTests
{
    private static readonly DateTime Now = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static TasksDbContext CreateContext(string databaseName, ITenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(databaseName).Options, tenantContext);

    [Fact]
    public async Task A_task_with_all_six_value_objects_round_trips()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        Guid taskId;

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var task = NewTask(tenantId, customerId);
            task.MoveToWaitingOnClient(
                ClientRequestNote.Create("falta W-2 y 1099-INT").Value,
                new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                Guid.NewGuid(),
                Now
            );
            taskId = task.Id;

            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var reloaded = await db.Tasks.SingleAsync(t => t.Id == taskId);

            Assert.Equal("Preparar 1040 de Pérez", reloaded.Title.Value);
            Assert.Equal("Revisar deducciones del año anterior.", reloaded.Description!.Value);
            Assert.Equal(12.5m, reloaded.Estimated!.Value);
            Assert.Equal("falta W-2 y 1099-INT", reloaded.ExpectedItems!.Value);

            Assert.Equal(new DateTime(2026, 4, 15, 3, 59, 0, DateTimeKind.Utc), reloaded.Due!.DueAtUtc);
            Assert.Equal("America/New_York", reloaded.Due.TimeZoneId);
            Assert.True(reloaded.Due.IsStatutory);

            Assert.Equal(customerId, reloaded.Reference.CustomerId);
            Assert.Equal(2025, reloaded.Reference.TaxYear);
        }
    }

    /// <summary>
    /// <see cref="TaskReference.None"/> tiene que volver como instancia, no como <c>null</c>: el
    /// agregado lo asume no-nulo.
    /// </summary>
    [Fact]
    public async Task A_task_without_customer_or_due_date_round_trips_too()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        var bare = TaskItem
            .Create(
                tenantId,
                Guid.NewGuid(),
                TaskTitle.Create("Ordenar el archivo").Value,
                null,
                TaskPriority.Low,
                TaskReference.None,
                null,
                null,
                null,
                Now
            )
            .Value;

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            db.Tasks.Add(bare);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName, tenantContext))
        {
            var reloaded = await db.Tasks.SingleAsync(t => t.Id == bare.Id);

            Assert.NotNull(reloaded.Reference);
            Assert.Null(reloaded.Reference.CustomerId);
            Assert.Null(reloaded.Reference.TaxYear);
            Assert.Null(reloaded.Due);
            Assert.Null(reloaded.Description);
            Assert.Null(reloaded.Estimated);
        }
    }

    [Fact]
    public async Task The_global_filter_hides_tasks_of_another_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        var seeder = new FakeTenantContext();
        seeder.SetTenant(mine);
        await using (var db = CreateContext(databaseName, seeder))
        {
            db.Tasks.Add(NewTask(mine, Guid.NewGuid()));
            db.Tasks.Add(NewTask(theirs, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        var reader = new FakeTenantContext();
        reader.SetTenant(mine);
        await using (var db = CreateContext(databaseName, reader))
        {
            var visible = await db.Tasks.ToListAsync();

            Assert.Single(visible);
            Assert.Equal(mine, visible[0].TenantId);
        }
    }

    /// <summary>Fail-closed: sin tenant en contexto devuelve nada, no todo.</summary>
    [Fact]
    public async Task Without_a_tenant_in_context_the_filter_returns_nothing()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();

        var seeder = new FakeTenantContext();
        seeder.SetTenant(tenantId);
        await using (var db = CreateContext(databaseName, seeder))
        {
            db.Tasks.Add(NewTask(tenantId, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName, new FakeTenantContext()))
        {
            Assert.Empty(await db.Tasks.ToListAsync());
        }
    }

    /// <summary>
    /// El repositorio usa tenant explícito porque los handlers corren en el scope de Wolverine, donde
    /// el filtro global devolvería 0 filas sobre datos que sí existen.
    /// </summary>
    [Fact]
    public async Task The_repository_reads_with_an_explicit_tenant_even_without_context()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();

        var seeder = new FakeTenantContext();
        seeder.SetTenant(tenantId);
        var task = NewTask(tenantId, Guid.NewGuid());
        await using (var db = CreateContext(databaseName, seeder))
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName, new FakeTenantContext()))
        {
            var repository = new TaskRepository(db);

            var found = await repository.GetByIdAsync(tenantId, task.Id);
            var foreignTenant = await repository.GetByIdAsync(Guid.NewGuid(), task.Id);

            Assert.True(found.IsSuccess);
            Assert.True(foreignTenant.IsFailure);
            Assert.Equal("Task.NotFound", foreignTenant.Error.Code);
        }
    }

    private static TaskItem NewTask(Guid tenantId, Guid customerId) =>
        TaskItem
            .Create(
                tenantId,
                Guid.NewGuid(),
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                TaskDescription.Create("Revisar deducciones del año anterior.").Value,
                TaskPriority.High,
                TaskReference.Create(customerId, 2025).Value,
                DueDate.Create(new DateTime(2026, 4, 15, 3, 59, 0, DateTimeKind.Utc), "America/New_York", true).Value,
                EstimatedHours.Create(12.5m).Value,
                Guid.NewGuid(),
                Now
            )
            .Value;
}
