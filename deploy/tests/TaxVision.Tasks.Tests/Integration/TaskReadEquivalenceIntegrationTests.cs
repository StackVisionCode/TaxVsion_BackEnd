using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Lecturas de tareas contra SQL Server comparadas con la regla anterior, escrita aquí en memoria: el recorte por
/// asignación (madre y abuela) y los cortes civiles del resumen y de la búsqueda por vencimiento. 240 tareas en tres
/// niveles, con clientes asignados, de un colega, sin asignar y sin cliente, y vencimientos alrededor de cada corte,
/// leídas por cinco alcances en cuatro zonas.
/// </summary>
public sealed class TaskReadEquivalenceIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly TaskItemStatus[] Open =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
    ];

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _myClientA = Guid.NewGuid();
    private readonly Guid _myClientB = Guid.NewGuid();
    private readonly Guid _colleagueClient = Guid.NewGuid();
    private readonly Guid _nobodysClient = Guid.NewGuid();
    private List<Row> _rows = [];

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var tasks = Hierarchy();
        await using (var db = _database.NewContext(_tenantId))
        {
            db.Tasks.AddRange(tasks);
            db.Set<CustomerAssignmentProjection>()
                .AddRange(
                    Assignments()
                        .Select(a => CustomerAssignmentProjection.Create(_tenantId, a.CustomerId, a.UserId, Now))
                );
            await db.SaveChangesAsync();
            await ScatterAsync(db, tasks);
        }
        await using var read = NoAmbientTenant();
        _rows = await read
            .Tasks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.TenantId == _tenantId)
            .Select(t => new Row(
                t.Id,
                t.Title.Value,
                t.ParentTaskId,
                t.AssigneeUserId,
                t.CreatedByUserId,
                t.Reference.CustomerId,
                t.Status,
                t.Due == null ? null : (DateTime?)t.Due.DueAtUtc,
                t.Due == null ? null : t.Due.TimeZoneId,
                t.Due != null && t.Due.IsStatutory,
                t.CompletedAtUtc
            ))
            .ToListAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    public static TheoryData<string> Zones =>
        ["America/New_York", "Asia/Tokyo", "Pacific/Kiritimati", "Pacific/Pago_Pago"];

    [Theory]
    [MemberData(nameof(Zones))]
    public async Task The_summary_matches_the_previous_rule_for_every_scope(string zoneId)
    {
        var window = TaskSummaryWindow.For(Now, ResolvedTimeZone.Resolve(zoneId));
        foreach (var viewer in Viewers())
        {
            await using var db = NoAmbientTenant();
            var summary = await new TaskSummaryReader(db).ReadAsync(
                new TaskSummaryFilter(_tenantId, null, null, null, viewer, window),
                CancellationToken.None
            );
            var open = Visible(viewer).Where(r => Open.Contains(r.Status)).ToList();
            var overdue = open.Where(r => Overdue(r, window)).ToList();
            var top = open.Where(r => Overdue(r, window) || DueToday(r, window))
                .OrderBy(r => r.DueAtUtc)
                .Take(TaskSummaryLimits.TopItems)
                .ToList();
            var deadlines = open.Where(r => r.IsStatutory && WithinDeadlines(r, window))
                .Select(r => (r.Title, DueOn: DueOn(r)))
                .Where(x => x.DueOn < window.DeadlinesEnd)
                .ToList();
            var lateOwners = overdue.Select(r => r.AssigneeUserId).Distinct().ToList();
            var because = $"{zoneId} / {Name(viewer)}";

            Assert.True(open.Count > 0, because);
            Assert.Equal((open.Count, overdue.Count), (summary.Open, summary.Overdue));
            Assert.Equal(
                (
                    open.Count(r => r.Status == TaskItemStatus.NotStarted),
                    open.Count(r => r.Status == TaskItemStatus.InProgress),
                    open.Count(r => r.Status == TaskItemStatus.WaitingOnClient)
                ),
                (summary.ByStatus.NotStarted, summary.ByStatus.InProgress, summary.ByStatus.WaitingOnClient)
            );
            Assert.Equal(open.Count(r => DueToday(r, window)), summary.DueToday);
            Assert.Equal(open.Count(r => DueSoon(r, window)), summary.DueSoon);
            Assert.Equal(overdue.Count(r => r.IsStatutory), summary.StatutoryOverdue);
            Assert.Equal(
                Visible(viewer)
                    .Count(r =>
                        r.Status == TaskItemStatus.Completed
                        && r.CompletedAtUtc >= window.TodayStartUtc
                        && r.CompletedAtUtc < window.TomorrowStartUtc
                    ),
                summary.CompletedToday
            );
            Assert.Equal(lateOwners.Count == 1 ? lateOwners[0] : null, summary.OverdueAssigneeUserId);
            // En un empate de vencimiento SQL Server ordena los Guid distinto que .NET: se comparan fechas y pertenencia.
            Assert.Equal(top.Select(r => r.DueAtUtc), summary.Top.Select(i => i.DueAtUtc));
            Assert.All(
                summary.Top,
                i => Assert.Contains(open, r => r.Id == i.Id && (Overdue(r, window) || DueToday(r, window)))
            );
            Assert.Equal(
                deadlines.OrderBy(x => x.Title).Select(x => (x.DueOn, x.Title, 1, 1)),
                summary.Deadlines.OrderBy(g => g.Title).Select(g => (g.DueOn, g.Title, g.ClientCount, g.TaskCount))
            );
        }
    }

    [Theory]
    [MemberData(nameof(Zones))]
    public async Task The_due_search_matches_the_previous_rule_for_every_scope(string zoneId)
    {
        var window = TaskSummaryWindow.For(Now, ResolvedTimeZone.Resolve(zoneId));
        foreach (var viewer in Viewers())
        foreach (var bucket in Enum.GetValues<TaskDueBucket>())
        {
            await using var db = NoAmbientTenant();
            var page = await new TaskRepository(db).SearchAsync(
                _tenantId,
                new TaskQueryFilter(Due: new TaskDueFilter(bucket, window)),
                1,
                1000,
                viewer
            );
            var expected = Visible(viewer)
                .Where(r => Open.Contains(r.Status) && InBucket(r, bucket, window))
                .Select(r => r.Id)
                .Order();

            Assert.Equal(expected, page.Items.Select(t => t.Id).Order());
            Assert.Equal(page.Items.Count, page.TotalCount);
        }
    }

    [Fact]
    public async Task Lists_detail_and_work_titles_match_the_previous_rule_for_every_scope()
    {
        foreach (var viewer in Viewers())
        {
            var visible = Visible(viewer).Select(r => r.Id).ToHashSet();
            await using var db = NoAmbientTenant();
            var repository = new TaskRepository(db);

            var search = await repository.SearchAsync(_tenantId, new TaskQueryFilter(), 1, 1000, viewer);
            var open = await repository.SearchAsync(_tenantId, new TaskQueryFilter(OnlyOpen: true), 1, 1000, viewer);
            var board = await repository.ListForBoardAsync(_tenantId, new TaskQueryFilter(), 1000, viewer);
            var byClient = await repository.ListByCustomerAsync(_tenantId, _myClientA, null, 1, 1000, viewer);
            var titles = await new TaskWorkReader(db).TitlesAsync(
                _tenantId,
                [.. _rows.Select(r => r.Id)],
                [],
                viewer,
                CancellationToken.None
            );

            Assert.Equal(visible.Order(), search.Items.Select(t => t.Id).Order());
            Assert.Equal(visible.Count, search.TotalCount);
            Assert.Equal(
                _rows.Where(r => visible.Contains(r.Id) && Open.Contains(r.Status)).Select(r => r.Id).Order(),
                open.Items.Select(t => t.Id).Order()
            );
            Assert.Equal(visible.Order(), board.Select(t => t.Id).Order());
            Assert.Equal(
                _rows.Where(r => visible.Contains(r.Id) && r.CustomerId == _myClientA).Select(r => r.Id).Order(),
                byClient.Items.Select(t => t.Id).Order()
            );
            Assert.Equal(visible.Order(), titles.Select(t => t.Id).Order());

            foreach (var parent in _rows.Where(r => r.ParentTaskId is null).Take(8))
            {
                var children = await repository.ListSubtasksAsync(_tenantId, parent.Id, 1, 100, viewer);
                Assert.Equal(
                    _rows.Where(r => r.ParentTaskId == parent.Id && visible.Contains(r.Id)).Select(r => r.Id).Order(),
                    children.Items.Select(t => t.Id).Order()
                );
            }

            foreach (var row in _rows)
                Assert.Equal(
                    visible.Contains(row.Id),
                    (await repository.GetByIdForReadAsync(_tenantId, row.Id, viewer)).IsSuccess
                );
        }
    }

    // ----- la regla anterior, en memoria -----

    private IEnumerable<Row> Visible(Guid? viewer)
    {
        if (viewer is not { } me)
            return _rows;
        var mine = Assignments().Where(a => a.UserId == me).Select(a => a.CustomerId).ToHashSet();
        var byId = _rows.ToDictionary(r => r.Id);
        bool Own(Row x) => x.AssigneeUserId == me || x.CreatedByUserId == me;
        bool OfMyClient(Row x) => x.CustomerId is { } c && mine.Contains(c);
        bool Pool(Row x) => x.CustomerId == null && x.AssigneeUserId == null;
        bool Grandparent(Row g) => Own(g) || OfMyClient(g) || Pool(g);
        bool Parent(Row p) =>
            Own(p)
            || OfMyClient(p)
            || (p.ParentTaskId == null && Pool(p))
            || (p.ParentTaskId is { } g && byId.TryGetValue(g, out var gp) && Grandparent(gp));
        return _rows.Where(t =>
            Own(t)
            || OfMyClient(t)
            || (t.ParentTaskId == null && Pool(t))
            || (t.ParentTaskId is { } p && byId.TryGetValue(p, out var parent) && Parent(parent))
        );
    }

    private static bool Midnight(Row r) => r.DueAtUtc!.Value.TimeOfDay == TimeSpan.Zero;

    private static bool Overdue(Row r, TaskSummaryWindow w) =>
        r.DueAtUtc is { } d && (Midnight(r) ? d < w.Work.TodayMidnightUtc : d < w.TodayStartUtc);

    private static bool DueToday(Row r, TaskSummaryWindow w) =>
        r.DueAtUtc is { } d
        && (Midnight(r) ? d == w.Work.TodayMidnightUtc : d >= w.TodayStartUtc && d < w.TomorrowStartUtc);

    private static bool DueSoon(Row r, TaskSummaryWindow w) =>
        r.DueAtUtc is { } d
        && (
            Midnight(r)
                ? d >= w.Work.TodayMidnightUtc && d < w.Work.DueSoonEndMidnightUtc
                : d >= w.Work.NowUtc && d < w.Work.DueSoonEndUtc
        );

    private static bool WithinDeadlines(Row r, TaskSummaryWindow w) =>
        r.DueAtUtc is { } d && (Midnight(r) ? d < w.DeadlinesEndMidnightUtc : d < w.DeadlinesEndUtc);

    private static bool InBucket(Row r, TaskDueBucket bucket, TaskSummaryWindow w) =>
        bucket switch
        {
            TaskDueBucket.Overdue => Overdue(r, w),
            TaskDueBucket.Today => DueToday(r, w),
            TaskDueBucket.Statutory => r.IsStatutory && WithinDeadlines(r, w),
            _ => DueSoon(r, w),
        };

    private static DateOnly DueOn(Row r)
    {
        var instant = DateTime.SpecifyKind(r.DueAtUtc!.Value, DateTimeKind.Utc);
        var due = DueDate.Create(instant, r.DueTimeZoneId, r.IsStatutory);
        return due.IsSuccess ? due.Value.DueOn : DateOnly.FromDateTime(instant);
    }

    // ----- datos -----

    private (Guid CustomerId, Guid UserId)[] Assignments() =>
        [(_myClientA, _me), (_myClientB, _me), (_colleagueClient, _colleague)];

    private IEnumerable<Guid?> Viewers() => [_me, _colleague, _admin, Guid.NewGuid(), null];

    private string Name(Guid? viewer) =>
        viewer == _me ? "me"
        : viewer == _colleague ? "colleague"
        : viewer == _admin ? "admin"
        : viewer is null ? "office"
        : "stranger";

    // 24 raíces (responsable × autor × cliente), 3 hijas por raíz y 2 nietas por hija.
    private List<TaskItem> Hierarchy()
    {
        var all = new List<TaskItem>();
        var n = 0;
        foreach (var assignee in new Guid?[] { _me, _colleague, null })
        foreach (var creator in new[] { _me, _admin })
        foreach (var customer in new Guid?[] { _myClientA, _colleagueClient, _nobodysClient, null })
        {
            var root = Root(++n, creator, customer, assignee);
            all.Add(root);
            foreach (var childAssignee in new Guid?[] { _me, _colleague, null })
            {
                var child = Child(root, ++n, _admin, childAssignee);
                all.Add(child);
                foreach (var grandchildAssignee in new Guid?[] { _colleague, null })
                    all.Add(Child(child, ++n, n % 4 == 0 ? _me : _admin, grandchildAssignee));
            }
        }
        return all;
    }

    // Estado, vencimiento, legal y cliente repartidos por posición (también subtareas con otro cliente que su madre).
    private async Task ScatterAsync(TasksDbContext db, List<TaskItem> tasks)
    {
        var window = TaskSummaryWindow.For(Now, ResolvedTimeZone.Resolve("America/New_York"));
        var midnight = window.Work.TodayMidnightUtc;
        DateTime?[] dues =
        [
            null,
            midnight.AddDays(-1),
            midnight,
            midnight.AddDays(1),
            midnight.AddDays(3),
            midnight.AddDays(10),
            midnight.AddDays(40),
            window.TodayStartUtc.AddHours(-1),
            window.TodayStartUtc.AddHours(1),
            Now.AddHours(1),
            Now.AddDays(2),
            Now.AddDays(8),
            window.DeadlinesEndUtc.AddHours(-1),
            window.DeadlinesEndUtc.AddHours(1),
            window.TomorrowStartUtc.AddMinutes(30),
            midnight.AddDays(-20),
            window.TodayStartUtc.AddDays(-3).AddMinutes(7),
        ];
        int[] statuses = [1, 2, 3, 4, 1, 2, 5, 3, 1];
        Guid?[] customers = [_myClientB, _colleagueClient, _nobodysClient, null];
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var status = statuses[i % statuses.Length];
            var due = dues[i % dues.Length];
            DateTime? completed =
                status == 4 ? (i % 2 == 0 ? window.TodayStartUtc.AddHours(2) : Now.AddDays(-2)) : null;
            var overrideCustomer = task.ParentTaskId is not null && i % 5 == 0;
            var customer = customers[i % customers.Length];
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE Tasks SET Status = {status}, CompletedAtUtc = {completed},
                    DueAtUtc = {due}, DueTimeZoneId = {(due is null ? null : "America/New_York")},
                    DueIsStatutory = {(due is null ? null : i % 3 == 0)},
                    CustomerId = CASE WHEN {overrideCustomer} = 1 THEN {customer} ELSE CustomerId END
                WHERE Id = {task.Id}
                """
            );
        }
    }

    private TaskItem Root(int n, Guid createdBy, Guid? customerId, Guid? assignee) =>
        TaskItem
            .Create(
                _tenantId,
                createdBy,
                TaskTitle.Create($"Task {n:000}").Value,
                null,
                TaskPriority.Normal,
                customerId is null ? TaskReference.None : TaskReference.Create(customerId, null).Value,
                null,
                null,
                assignee,
                Now
            )
            .Value;

    private static TaskItem Child(TaskItem parent, int n, Guid createdBy, Guid? assignee) =>
        TaskItem
            .CreateSubtask(
                parent,
                createdBy,
                TaskTitle.Create($"Task {n:000}").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                assignee,
                Now
            )
            .Value;

    private TasksDbContext NoAmbientTenant() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(_database.ConnectionString).Options,
            new NoTenant()
        );

    private sealed record Row(
        Guid Id,
        string Title,
        Guid? ParentTaskId,
        Guid? AssigneeUserId,
        Guid CreatedByUserId,
        Guid? CustomerId,
        TaskItemStatus Status,
        DateTime? DueAtUtc,
        string? DueTimeZoneId,
        bool IsStatutory,
        DateTime? CompletedAtUtc
    );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
