using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// El aislamiento real no lo da el filtro global: **todos** los repositorios llaman
/// <c>IgnoreQueryFilters()</c> —los handlers corren en el scope de Wolverine, sin
/// <c>TenantContext</c>— y lo sustituyen por el tenant explícito en el predicado. Los tests del
/// filtro global van contra <c>db.Tasks</c> y no tocan ese camino, así que no prueban lo que usan
/// los endpoints.
///
/// <para>
/// Estos van por el camino real, contra SQL Server, con dos tenants que comparten forma: mismo
/// asignado, mismo cliente, mismo código de label. Si un predicado pierde su <c>TenantId</c>, acá se
/// ve; en InMemory no, porque evalúa en memoria.
/// </para>
/// </summary>
public sealed class TaskTenantIsolationTests
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
    public async Task No_read_of_the_task_repository_crosses_the_tenant()
    {
        var world = await SeedTwoTenantsAsync();

        await using var db = CreateContext();
        var repository = new TaskRepository(db);
        var mine = world.Mine;

        Assert.True((await repository.GetByIdAsync(mine, world.TheirTask.Id)).IsFailure);
        Assert.True((await repository.GetByIdWithTimersAsync(mine, world.TheirTask.Id)).IsFailure);
        Assert.Empty((await repository.ListSubtasksAsync(mine, world.TheirParent.Id, 1, 50)).Items);
        Assert.Empty((await repository.ListByIdsAsync(mine, [world.TheirTask.Id])));
        Assert.Empty(await repository.ListChildIdsAsync(mine, [world.TheirParent.Id]));
        Assert.Empty(await repository.GetAncestorIdsAsync(mine, world.TheirChild.Id));

        var byAssignee = await repository.ListForAssigneeAsync(mine, world.SharedAssignee, null, 1, 50);
        var byCustomer = await repository.ListByCustomerAsync(mine, world.SharedCustomer, null, 1, 50);
        var waiting = await repository.ListWaitingOnClientAsync(mine, 1, 50);

        Assert.All(byAssignee.Items, t => Assert.Equal(mine, t.TenantId));
        Assert.All(byCustomer.Items, t => Assert.Equal(mine, t.TenantId));
        Assert.All(waiting.Items, t => Assert.Equal(mine, t.TenantId));
        Assert.NotEmpty(byCustomer.Items);
    }

    /// <summary>
    /// El tablero, el calendario y la búsqueda son de la Fase 7 y filtran por columnas de un owned
    /// type o por texto; son las tres formas más fáciles de perder el <c>TenantId</c> al armar el
    /// predicado por partes.
    /// </summary>
    [Fact]
    public async Task The_board_the_calendar_and_the_search_stay_inside_the_tenant()
    {
        var world = await SeedTwoTenantsAsync();

        await using var db = CreateContext();
        var repository = new TaskRepository(db);
        var mine = world.Mine;

        var board = await repository.ListForBoardAsync(mine, new TaskQueryFilter(OnlyOpen: true), 500);
        var calendar = await repository.ListForCalendarAsync(mine, Now.AddDays(-30), Now.AddDays(30), null, 500);
        var search = await repository.SearchAsync(mine, new TaskQueryFilter(SharedTitle), 1, 50);

        Assert.All(board, t => Assert.Equal(mine, t.TenantId));
        Assert.All(calendar, t => Assert.Equal(mine, t.TenantId));
        Assert.All(search.Items, t => Assert.Equal(mine, t.TenantId));
        Assert.NotEmpty(search.Items);
    }

    /// <summary>
    /// La consulta del grafo es SQL recursivo con <c>@tenantId</c> parametrizado a mano: si ese
    /// parámetro se cayera, un tenant vería las dependencias de otro y ningún test de LINQ lo notaría.
    /// </summary>
    [Fact]
    public async Task The_dependency_graph_does_not_leak_edges_of_another_tenant()
    {
        var world = await SeedTwoTenantsAsync();

        await using var db = CreateContext();
        var repository = new TaskDependencyRepository(db);
        var mine = world.Mine;

        Assert.Null(await repository.GetAsync(mine, world.TheirChild.Id, world.TheirParent.Id));
        Assert.Empty(await repository.ListSuccessorIdsAsync(mine, world.TheirParent.Id));
        Assert.Empty(await repository.LoadUpstreamGraphAsync(mine, world.TheirChild.Id));
        Assert.NotEmpty(await repository.LoadUpstreamGraphAsync(mine, world.MyChild.Id));
    }

    /// <summary>
    /// Los timers no llevan tenant propio: lo heredan de su tarea. Si el reporte dejara de arrancar
    /// desde <c>Tasks</c>, sumaría las horas de toda la instalación.
    /// </summary>
    [Fact]
    public async Task The_timer_report_only_counts_hours_of_the_asked_tenant()
    {
        var world = await SeedTwoTenantsAsync();

        await using var db = CreateContext();
        var rows = await new TaskTimerRepository(db).ListReportAsync(world.Mine, Now.AddDays(-1), Now.AddDays(1), null);

        Assert.Equal([world.MyTask.Id], rows.Select(r => r.TaskId).ToArray());
    }

    /// <summary>
    /// <c>CodeExistsAsync</c> se mira en los dos sentidos: no debe ver el código que solo tiene el
    /// otro tenant —sería un 409 imposible de explicar— pero sí el propio, que es para lo que existe.
    /// </summary>
    [Fact]
    public async Task The_label_catalog_is_per_tenant_in_both_directions()
    {
        var world = await SeedTwoTenantsAsync();

        await using var db = CreateContext();
        var repository = new TaskLabelRepository(db);
        var mine = world.Mine;

        Assert.True((await repository.GetByIdAsync(mine, world.TheirLabel.Id)).IsFailure);
        Assert.False(await repository.CodeExistsAsync(mine, OnlyTheirsCode, null));
        Assert.True(await repository.CodeExistsAsync(mine, SharedCode, null));
        Assert.Equal([world.MyLabel.Id], (await repository.ListAsync(mine)).Select(l => l.Id).ToArray());
    }

    private const string SharedTitle = "Preparar 1040 de Pérez";

    private static TaskLabelCode SharedCode => TaskLabelCode.Create("waiting_docs").Value;

    private static TaskLabelCode OnlyTheirsCode => TaskLabelCode.Create("their_own_code").Value;

    private sealed record TwoTenants(
        Guid Mine,
        Guid SharedAssignee,
        Guid SharedCustomer,
        TaskItem MyTask,
        TaskItem MyChild,
        TaskItem TheirTask,
        TaskItem TheirParent,
        TaskItem TheirChild,
        TaskLabel MyLabel,
        TaskLabel TheirLabel
    );

    /// <summary>
    /// Dos tenants con la misma forma: mismo asignado, mismo cliente, mismo título y mismo código de
    /// label. Sin esa coincidencia el test pasaría aunque el predicado filtrara por otra columna.
    /// </summary>
    private static async Task<TwoTenants> SeedTwoTenantsAsync()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var customer = Guid.NewGuid();

        var myTask = NewTask(mine, assignee, customer);
        var myParent = NewTask(mine, assignee, customer);
        var myChild = NewTask(mine, assignee, customer);
        var theirTask = NewTask(theirs, assignee, customer);
        var theirParent = NewTask(theirs, assignee, customer);
        var theirChild = NewTask(theirs, assignee, customer);

        myTask.MoveToWaitingOnClient(ClientRequestNote.Create("falta W-2").Value, null, assignee, Now);
        theirTask.MoveToWaitingOnClient(ClientRequestNote.Create("falta W-2").Value, null, assignee, Now);

        var timer = myTask.StartTimer(assignee, isBillable: true, Now).Value;
        myTask.StopTimer(timer.Id, assignee, Now.AddHours(1));
        var theirTimer = theirTask.StartTimer(assignee, isBillable: true, Now).Value;
        theirTask.StopTimer(theirTimer.Id, assignee, Now.AddHours(1));

        var myLabel = NewLabel(mine, SharedCode);
        var theirLabel = NewLabel(theirs, SharedCode);

        await using var db = CreateContext();
        db.Tasks.AddRange(myTask, myParent, myChild, theirTask, theirParent, theirChild);
        db.TaskLabels.AddRange(myLabel, theirLabel, NewLabel(theirs, OnlyTheirsCode));
        db.TaskDependencies.AddRange(
            TaskDependency.Create(mine, myChild.Id, myParent.Id, assignee, Now).Value,
            TaskDependency.Create(theirs, theirChild.Id, theirParent.Id, assignee, Now).Value
        );
        await db.SaveChangesAsync();

        return new TwoTenants(
            mine,
            assignee,
            customer,
            myTask,
            myChild,
            theirTask,
            theirParent,
            theirChild,
            myLabel,
            theirLabel
        );
    }

    private static TaskLabel NewLabel(Guid tenantId, TaskLabelCode code) =>
        TaskLabel
            .Create(
                tenantId,
                code,
                "Esperando documentos",
                LabelColor.Create("#2E7D32").Value,
                TaskItemStatus.NotStarted,
                1
            )
            .Value;

    private static TaskItem NewTask(Guid tenantId, Guid assigneeUserId, Guid customerId) =>
        TaskItem
            .Create(
                tenantId,
                assigneeUserId,
                TaskTitle.Create(SharedTitle).Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(customerId, 2026).Value,
                DueDate.Create(Now.AddDays(3), "America/New_York", false).Value,
                null,
                assigneeUserId,
                Now
            )
            .Value;
}
