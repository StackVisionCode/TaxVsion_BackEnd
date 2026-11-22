using BuildingBlocks.Contracts.Work;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Work;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Conteos y top de trabajo traducidos por SQL Server: vencido/próximo por el día de la oficina (Nueva York), el ABAC
/// del que mira también en los números, y nada de otro tenant.
/// </summary>
public sealed class TaskWorkReaderIntegrationTests : IAsyncLifetime
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    // 10:00 en Nueva York del 18.
    private static readonly WorkWindow Window = WorkWindow.For(
        new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc),
        NewYork
    );

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _luis = Guid.NewGuid();
    private readonly Guid _boss = Guid.NewGuid();
    private readonly Guid _creator = Guid.NewGuid();
    private readonly Guid _bossClient = Guid.NewGuid();
    private readonly Guid _otherClient = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        var now = Window.NowUtc.AddDays(-10);
        var waiting = NewTask(_tenantId, "Waiting", _ana, null, null);
        Assert.True(waiting.MoveToWaitingOnClient(ClientRequestNote.Create("W-2").Value, null, _ana, now).IsSuccess);
        var completed = NewTask(_tenantId, "Completed", _ana, null, Civil(Window.Today.AddDays(-3)));
        Assert.True(completed.Complete(_ana, now).IsSuccess);

        await using var db = _database.NewContext(_tenantId);
        db.Tasks.AddRange(
            NewTask(_tenantId, "Late civil", _ana, _bossClient, Civil(Window.Today.AddDays(-2))),
            NewTask(_tenantId, "Due today", _ana, null, Civil(Window.Today)),
            NewTask(_tenantId, "Timed this morning", _ana, null, Timed(Window.NowUtc.AddHours(-2))),
            NewTask(_tenantId, "Legacy today", _ana, null, Timed(Window.TodayMidnightUtc)),
            waiting,
            NewTask(_tenantId, "Far away", _ana, _otherClient, Civil(Window.Today.AddDays(30))),
            completed,
            NewTask(_tenantId, "Luis late", _luis, null, Civil(Window.Today.AddDays(-1))),
            NewTask(_otherTenantId, "Other tenant", _ana, _bossClient, Civil(Window.Today.AddDays(-4)))
        );
        db.ClientRequests.AddRange(
            Request(_tenantId, _bossClient, _ana, null, Window.Today.AddDays(-1)),
            Request(_tenantId, _otherClient, _luis, _ana, Window.Today.AddDays(2)),
            Request(_tenantId, _otherClient, _ana, _luis, null),
            Request(_otherTenantId, _bossClient, _ana, null, Window.Today.AddDays(-1))
        );
        db.Set<CustomerAssignmentProjection>()
            .AddRange(
                CustomerAssignmentProjection.Create(_tenantId, _bossClient, _boss, now),
                CustomerAssignmentProjection.Create(_otherTenantId, _otherClient, _boss, now)
            );
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Counts_by_the_office_day_for_an_unrestricted_viewer()
    {
        await using var db = _database.NewContext(Guid.Empty);

        var counts = await new TaskWorkReader(db).CountAsync(_tenantId, _ana, null, Window, default);

        Assert.Equal(
            [new WorkCount(WorkKinds.Tasks, 6, 2, 2, 1), new WorkCount(WorkKinds.ClientRequests, 2, 1, 1, 2)],
            counts.ToArray()
        );
    }

    [Fact]
    public async Task A_restricted_viewer_only_counts_what_he_can_see()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var reader = new TaskWorkReader(db);

        var counts = await reader.CountAsync(_tenantId, _ana, _boss, Window, default);
        var top = await reader.TopAsync(_tenantId, _ana, _boss, WorkLimits.TopItems, default);

        Assert.Equal(
            [new WorkCount(WorkKinds.Tasks, 1, 1, 0, 0), new WorkCount(WorkKinds.ClientRequests, 1, 1, 0, 1)],
            counts.ToArray()
        );
        Assert.Equal(["Late civil", "W-2 2025"], top.Select(i => i.Title).Order().ToArray());
        Assert.All(top, item => Assert.Equal(_bossClient, item.CustomerId));
    }

    [Fact]
    public async Task Top_items_carry_the_CRM_link_and_the_right_kind_of_due()
    {
        await using var db = _database.NewContext(Guid.Empty);

        var top = await new TaskWorkReader(db).TopAsync(_tenantId, _ana, null, WorkLimits.TopItems, default);

        var late = Assert.Single(top, i => i.Title == "Late civil");
        Assert.Equal(WorkKinds.Tasks, late.Kind);
        Assert.Equal($"/task?taskId={late.Id}", late.Link);
        Assert.Equal(Window.Today.AddDays(-2), late.DueDate);
        Assert.Null(late.DueAtUtc);
        var timed = Assert.Single(top, i => i.Title == "Timed this morning");
        Assert.Equal(Window.NowUtc.AddHours(-2), timed.DueAtUtc);
        Assert.Null(timed.DueDate);
        var request = Assert.Single(top, i => i.Kind == WorkKinds.ClientRequests && i.DueDate < Window.Today);
        Assert.Equal($"/clients/{_bossClient}?tab=work&requestId={request.Id}", request.Link);
        Assert.DoesNotContain(top, i => i.Title is "Completed" or "Luis late" or "Other tenant");
    }

    [Fact]
    public async Task Batch_groups_by_person_in_one_query_and_never_crosses_tenants()
    {
        await using var db = _database.NewContext(Guid.Empty);

        var rows = await new TaskWorkReader(db).CountByUserAsync(
            _tenantId,
            [_ana, _luis, Guid.NewGuid()],
            null,
            Window,
            default
        );

        Assert.Equal(
            [new WorkBatchRow(_ana, 8, 3), new WorkBatchRow(_luis, 2, 1)],
            rows.OrderBy(r => r.Open).Reverse().ToArray()
        );
    }

    [Fact]
    public async Task Titles_only_come_back_for_what_the_viewer_can_read()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var tasks = await db.Tasks.IgnoreQueryFilters().ToDictionaryAsync(t => t.Title.Value, t => t.Id);
        var requests = await db
            .ClientRequests.IgnoreQueryFilters()
            .Select(r => new
            {
                r.Id,
                r.TenantId,
                r.CustomerId,
            })
            .ToListAsync();
        Guid[] taskIds =
        [
            tasks["Late civil"],
            tasks["Far away"],
            tasks["Completed"],
            tasks["Other tenant"],
            Guid.NewGuid(),
        ];
        var requestIds = requests.Select(r => r.Id).ToArray();
        var reader = new TaskWorkReader(db);

        var restricted = await reader.TitlesAsync(_tenantId, taskIds, requestIds, _boss, default);
        var full = await reader.TitlesAsync(_tenantId, taskIds, requestIds, null, default);

        var bossRequest = requests.Single(r => r.TenantId == _tenantId && r.CustomerId == _bossClient).Id;
        Assert.Equal(
            [(WorkResourceTypes.ClientRequest, bossRequest), (WorkResourceTypes.Task, tasks["Late civil"])],
            restricted.Select(t => (t.ResourceType, t.Id)).Order().ToArray()
        );
        Assert.Equal(
            ["Completed", "Far away", "Late civil"],
            full.Where(t => t.ResourceType == WorkResourceTypes.Task).Select(t => t.Title).Order().ToArray()
        );
        Assert.Equal(3, full.Count(t => t.ResourceType == WorkResourceTypes.ClientRequest));
    }

    [Fact]
    public async Task Items_page_the_open_universe_most_urgent_first()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var reader = new TaskWorkReader(db);
        var counts = await reader.CountAsync(_tenantId, _ana, null, Window, default);

        var first = await reader.ItemsAsync(_tenantId, _ana, null, WorkKinds.Tasks, 1, 4, default);
        var second = await reader.ItemsAsync(_tenantId, _ana, null, WorkKinds.Tasks, 2, 4, default);
        var requests = await reader.ItemsAsync(_tenantId, _ana, _boss, WorkKinds.ClientRequests, 1, 20, default);

        Assert.Equal(counts.Single(c => c.Kind == WorkKinds.Tasks).Open, first.TotalCount);
        Assert.Equal((1, 4, 6), (first.Page, first.Size, second.TotalCount));
        Assert.Equal(
            ["Late civil", "Legacy today", "Timed this morning", "Due today", "Far away", "Waiting"],
            first.Items.Concat(second.Items).Select(i => i.Title).ToArray()
        );
        Assert.All(first.Items, item => Assert.Equal(WorkKinds.Tasks, item.Kind));
        var request = Assert.Single(requests.Items);
        Assert.Equal(
            (1, WorkKinds.ClientRequests, _bossClient),
            (requests.TotalCount, request.Kind, request.CustomerId)
        );
    }

    [Fact]
    public async Task Office_waiting_counts_everyone_but_only_what_the_viewer_can_see()
    {
        await using var db = _database.NewContext(Guid.Empty);
        var reader = new TaskWorkReader(db);

        var full = await reader.CountOfficeWaitingAsync(_tenantId, null, default);
        var restricted = await reader.CountOfficeWaitingAsync(_tenantId, _boss, default);

        // Sin recorte: la tarea en espera de Ana y los tres pedidos pendientes (de Ana y de Luis).
        Assert.Equal(
            [new OfficeWaitingCount(WorkKinds.Tasks, 1), new OfficeWaitingCount(WorkKinds.ClientRequests, 3)],
            full.ToArray()
        );
        // El jefe solo ve su cliente: la tarea sin cliente es de Ana y no cuenta.
        Assert.Equal(
            [new OfficeWaitingCount(WorkKinds.Tasks, 0), new OfficeWaitingCount(WorkKinds.ClientRequests, 1)],
            restricted.ToArray()
        );
    }

    private static DueDate Civil(DateOnly day) => DueDate.EndOfCivilDay(day, NewYork, false).Value;

    private static DueDate Timed(DateTime dueAtUtc) => DueDate.Create(dueAtUtc, "America/New_York", false).Value;

    private TaskItem NewTask(Guid tenantId, string title, Guid assignee, Guid? customerId, DueDate? due) =>
        TaskItem
            .Create(
                tenantId,
                _creator,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                customerId is null ? TaskReference.None : TaskReference.Create(customerId, null).Value,
                due,
                null,
                assignee,
                Window.NowUtc.AddDays(-20)
            )
            .Value;

    private static ClientRequest Request(
        Guid tenantId,
        Guid customerId,
        Guid requestedBy,
        Guid? followUp,
        DateOnly? dueOn
    )
    {
        var request = ClientRequest
            .Create(
                tenantId,
                customerId,
                requestedBy,
                null,
                "W-2 2025",
                null,
                dueOn is { } day ? LocalDay.EndUtc(day, NewYork) : null,
                Window.NowUtc.AddDays(-5),
                dueOn
            )
            .Value;
        if (followUp is { } user)
            Assert.True(request.HandOverFollowUp(user).IsSuccess);
        return request;
    }
}
