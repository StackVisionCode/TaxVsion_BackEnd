using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Las lecturas del repositorio contra SQL Server real: las tres primeras filtran u ordenan por
/// columnas que viven dentro de un owned type, y ahí es donde la traducción de LINQ se rompe.
/// InMemory evalúa en memoria y contestaría bien aunque el SQL no exista.
/// </summary>
public sealed class TaskRepositoryQueryTests
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

    /// <summary>
    /// Por vencimiento ascendente y las sin fecha al final. En SQL Server los NULL ordenan primero en
    /// ASC, así que quitar el primer criterio invierte la lista sin lanzar ninguna excepción.
    /// </summary>
    [Fact]
    public async Task My_tasks_are_ordered_by_due_date_with_undated_ones_last()
    {
        var tenantId = Guid.NewGuid();
        var assignee = Guid.NewGuid();

        var late = NewTask(tenantId, assignee, due: Due(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        var soon = NewTask(tenantId, assignee, due: Due(new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc)));
        var undated = NewTask(tenantId, assignee);

        await SeedAsync(late, undated, soon);

        await using var db = CreateContext();
        var page = await new TaskRepository(db).ListForAssigneeAsync(tenantId, assignee, null, 1, 10);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([soon.Id, late.Id, undated.Id], page.Items.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task My_tasks_excludes_closed_ones_unless_a_status_is_asked_for()
    {
        var tenantId = Guid.NewGuid();
        var assignee = Guid.NewGuid();

        var open = NewTask(tenantId, assignee);
        var done = NewTask(tenantId, assignee);
        done.Complete(assignee, Now);

        await SeedAsync(open, done);

        await using var db = CreateContext();
        var repository = new TaskRepository(db);

        var defaultView = await repository.ListForAssigneeAsync(tenantId, assignee, null, 1, 10);
        var completedOnly = await repository.ListForAssigneeAsync(tenantId, assignee, TaskItemStatus.Completed, 1, 10);

        Assert.Equal([open.Id], defaultView.Items.Select(t => t.Id).ToArray());
        Assert.Equal([done.Id], completedOnly.Items.Select(t => t.Id).ToArray());
    }

    /// <summary>El filtro por cliente y año atraviesa el owned type <c>Reference</c>.</summary>
    [Fact]
    public async Task By_customer_can_narrow_to_a_single_tax_year()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var year2025 = NewTask(tenantId, Guid.NewGuid(), reference: Reference(customerId, 2025));
        var year2026 = NewTask(tenantId, Guid.NewGuid(), reference: Reference(customerId, 2026));
        var otherCustomer = NewTask(tenantId, Guid.NewGuid(), reference: Reference(Guid.NewGuid(), 2025));

        await SeedAsync(year2025, year2026, otherCustomer);

        await using var db = CreateContext();
        var repository = new TaskRepository(db);

        var wholeCustomer = await repository.ListByCustomerAsync(tenantId, customerId, null, 1, 10);
        var justOneYear = await repository.ListByCustomerAsync(tenantId, customerId, 2025, 1, 10);

        Assert.Equal(2, wholeCustomer.TotalCount);
        Assert.Equal([year2025.Id], justOneYear.Items.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Waiting_on_client_is_ordered_by_what_the_client_owes_us_first()
    {
        var tenantId = Guid.NewGuid();
        var user = Guid.NewGuid();

        var noDeadline = NewTask(tenantId, user);
        var friday = NewTask(tenantId, user);
        var nextMonth = NewTask(tenantId, user);
        var notWaiting = NewTask(tenantId, user);

        noDeadline.MoveToWaitingOnClient(Note("falta W-2"), null, user, Now);
        friday.MoveToWaitingOnClient(
            Note("falta 1099-INT"),
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            user,
            Now
        );
        nextMonth.MoveToWaitingOnClient(
            Note("falta K-1"),
            new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
            user,
            Now
        );

        await SeedAsync(noDeadline, friday, nextMonth, notWaiting);

        await using var db = CreateContext();
        var page = await new TaskRepository(db).ListWaitingOnClientAsync(tenantId, 1, 10);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([friday.Id, nextMonth.Id, noDeadline.Id], page.Items.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// Cross-tenant. Cubre las dos mitades: que ve tareas de tenants distintos y que no arrastra las
    /// que ya están cerradas.
    /// </summary>
    [Fact]
    public async Task The_overdue_sweep_crosses_tenants_and_skips_closed_tasks()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var user = Guid.NewGuid();
        var longAgo = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var overdueA = NewTask(tenantA, user, due: Due(longAgo));
        var overdueB = NewTask(tenantB, user, due: Due(longAgo));
        var overdueButDone = NewTask(tenantA, user, due: Due(longAgo));
        overdueButDone.Complete(user, Now);
        var future = NewTask(tenantA, user, due: Due(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await SeedAsync(overdueA, overdueB, overdueButDone, future);

        await using var db = CreateContext();

        // El barrido es cross-tenant y la base es compartida: con un take chico, las filas que otras
        // corridas dejaron vencidas desplazan a las de este test fuera del tope y la asercion falla
        // sola con el tiempo. Se pide un lote amplio y se mira solo lo sembrado aqui.
        var found = await new TaskRepository(db).ListOverdueAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            10_000
        );

        var ids = found.Where(t => t.TenantId == tenantA || t.TenantId == tenantB).Select(t => t.Id).ToHashSet();
        Assert.Contains(overdueA.Id, ids);
        Assert.Contains(overdueB.Id, ids);
        Assert.DoesNotContain(overdueButDone.Id, ids);
        Assert.DoesNotContain(future.Id, ids);
    }

    /// <summary>
    /// El título es un VO con <c>HasConversion</c>: el <c>Contains</c> se escribe sobre
    /// <c>Title.Value</c> y sólo se sabe si EF lo traduce ejecutándolo. InMemory lo evaluaría en
    /// memoria y pasaría igual con SQL imposible.
    /// </summary>
    [Fact]
    public async Task Search_matches_part_of_the_title_in_sql()
    {
        var tenantId = Guid.NewGuid();
        var assignee = Guid.NewGuid();

        var wanted = NewTask(tenantId, assignee, title: "Preparar 1040 de Pérez");
        var other = NewTask(tenantId, assignee, title: "Conciliar nómina de Gómez");

        await SeedAsync(wanted, other);

        await using var db = CreateContext();
        var page = await new TaskRepository(db).SearchAsync(tenantId, new TaskQueryFilter("1040"), 1, 10);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(wanted.Id, page.Items[0].Id);
    }

    [Fact]
    public async Task The_board_only_brings_open_tasks_of_the_asked_assignee()
    {
        var tenantId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        var open = NewTask(tenantId, mine);
        var closed = NewTask(tenantId, mine);
        closed.Complete(mine, Now);
        var foreign = NewTask(tenantId, someoneElse);

        await SeedAsync(open, closed, foreign);

        await using var db = CreateContext();
        var filter = new TaskQueryFilter(AssigneeUserId: mine, OnlyOpen: true);
        var items = await new TaskRepository(db).ListForBoardAsync(tenantId, filter, 50);

        Assert.Equal([open.Id], items.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// El rango filtra por <c>DueAtUtc</c>, que vive dentro de un owned type: si la traducción se
    /// rompe, el calendario devuelve todo o nada.
    /// </summary>
    [Fact]
    public async Task The_calendar_only_brings_what_falls_inside_the_range()
    {
        var tenantId = Guid.NewGuid();
        var assignee = Guid.NewGuid();

        var inside = NewTask(tenantId, assignee, due: Due(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc)));
        var outside = NewTask(tenantId, assignee, due: Due(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc)));
        var undated = NewTask(tenantId, assignee);

        await SeedAsync(inside, outside, undated);

        await using var db = CreateContext();
        var items = await new TaskRepository(db).ListForCalendarAsync(
            tenantId,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            null,
            50
        );

        Assert.Equal([inside.Id], items.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Subtasks_bring_only_the_direct_children_of_the_asked_parent()
    {
        var tenantId = Guid.NewGuid();
        var assignee = Guid.NewGuid();

        var parent = NewTask(tenantId, assignee);
        var child = Subtask(parent, assignee);
        var otherRoot = NewTask(tenantId, assignee);

        await SeedAsync(parent, child, otherRoot);

        await using var db = CreateContext();
        var page = await new TaskRepository(db).ListSubtasksAsync(tenantId, parent.Id, 1, 10);

        Assert.Equal([child.Id], page.Items.Select(t => t.Id).ToArray());
    }

    private static TaskItem Subtask(TaskItem parent, Guid assigneeUserId) =>
        TaskItem
            .CreateSubtask(
                parent,
                assigneeUserId,
                TaskTitle.Create("Juntar los W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                assigneeUserId,
                Now
            )
            .Value;

    private static async Task SeedAsync(params TaskItem[] tasks)
    {
        await using var db = CreateContext();
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync();
    }

    private static DueDate Due(DateTime dueAtUtc) => DueDate.Create(dueAtUtc, "America/New_York", false).Value;

    private static TaskReference Reference(Guid customerId, int taxYear) =>
        TaskReference.Create(customerId, taxYear).Value;

    private static ClientRequestNote Note(string value) => ClientRequestNote.Create(value).Value;

    private static TaskItem NewTask(
        Guid tenantId,
        Guid assigneeUserId,
        DueDate? due = null,
        TaskReference? reference = null,
        string title = "Query probe"
    ) =>
        TaskItem
            .Create(
                tenantId,
                assigneeUserId,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                reference ?? TaskReference.None,
                due,
                null,
                assigneeUserId,
                Now
            )
            .Value;
}
