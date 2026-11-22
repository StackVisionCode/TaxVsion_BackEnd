using System.Reflection;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Resumen del dashboard: vencida y "hoy" por la fecha civil de la oficina, el recorte del que mira en todos los
/// números, el top acotado y los vencimientos legales agrupados por fecha y título.
/// </summary>
public sealed class TaskSummaryReaderTests
{
    [Fact]
    public async Task Counts_follow_the_office_day_and_the_viewer_scope()
    {
        await using var db = NewContext();
        var scenario = new TaskSummaryScenario();
        await scenario.SeedAsync(db);

        await scenario.AssertAsync(new TaskSummaryReader(db));
    }

    [Fact]
    public async Task The_filters_of_the_board_narrow_every_number()
    {
        await using var db = NewContext();
        var scenario = new TaskSummaryScenario();
        await scenario.SeedAsync(db);

        var luis = await new TaskSummaryReader(db).ReadAsync(
            scenario.Filter(null) with
            {
                AssigneeUserId = scenario.Luis,
            },
            default
        );

        Assert.Equal((1, 1, 0, 0), (luis.Open, luis.Overdue, luis.DueToday, luis.CompletedToday));
        Assert.Equal(scenario.Luis, luis.OverdueAssigneeUserId);
    }

    // InMemory oculta lo que SQL Server no traduce (owned Due dentro de un GROUP BY): se genera el SQL real de cada
    // consulta del lector sin conectarse.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Summary_queries_translate_to_sql_server(bool restricted)
    {
        using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=translation_only;Trusted_Connection=True;TrustServerCertificate=True"
                )
                .Options,
            new NoTenant()
        );
        var scenario = new TaskSummaryScenario();
        var filter = scenario.Filter(restricted ? scenario.Boss : null) with
        {
            CustomerId = Guid.NewGuid(),
            TaxYear = 2025,
        };
        var reader = new TaskSummaryReader(db);

        var queries = typeof(TaskSummaryReader)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => m.IsAssembly && typeof(IQueryable).IsAssignableFrom(m.ReturnType))
            .Select(m => (m.Name, Sql: ((IQueryable)m.Invoke(reader, [filter])!).ToQueryString()))
            .ToList();

        Assert.Equal(
            [
                "ByStatus",
                "Completed",
                "Deadlines",
                "DueSoon",
                "DueToday",
                "Overdue",
                "OverdueAssignees",
                "StatutoryOverdue",
                "Top",
            ],
            queries.Select(q => q.Name).Order()
        );
        Assert.All(queries, q => Assert.Contains("[Tasks]", q.Sql, StringComparison.Ordinal));
        Assert.Contains("GROUP BY", queries.Single(q => q.Name == "ByStatus").Sql, StringComparison.Ordinal);
    }

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}

/// <summary>Escenario compartido con la prueba de integración: los mismos números en InMemory y en SQL Server.</summary>
internal sealed class TaskSummaryScenario
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    // 10:00 en Nueva York del 18.
    public static readonly TaskSummaryWindow Window = TaskSummaryWindow.For(
        new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc),
        NewYork
    );

    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid Ana { get; } = Guid.NewGuid();
    public Guid Luis { get; } = Guid.NewGuid();
    public Guid Boss { get; } = Guid.NewGuid();
    private readonly Guid _creator = Guid.NewGuid();
    private readonly Guid _bossClient = Guid.NewGuid();
    private readonly Guid _otherClient = Guid.NewGuid();

    private DateOnly Today => Window.Today;
    private DateTime Now => Window.Work.NowUtc;

    public TaskSummaryFilter Filter(Guid? visibleTo) => new(TenantId, null, null, null, visibleTo, Window);

    public async Task SeedAsync(TasksDbContext db)
    {
        var waiting = NewTask(TenantId, "Waiting", Ana, null, null);
        Assert.True(waiting.MoveToWaitingOnClient(ClientRequestNote.Create("W-2").Value, null, Ana, Now).IsSuccess);
        var doneToday = NewTask(TenantId, "Done today", Ana, _bossClient, Civil(Today));
        Assert.True(doneToday.Complete(Ana, Now.AddHours(-1)).IsSuccess);
        var doneYesterday = NewTask(TenantId, "Done yesterday", Ana, _bossClient, Civil(Today));
        Assert.True(doneYesterday.Complete(Ana, Window.TodayStartUtc.AddHours(-1)).IsSuccess);

        db.Tasks.AddRange(
            NewTask(TenantId, "Late", Ana, _bossClient, Civil(Today.AddDays(-2))),
            NewTask(TenantId, "Luis late", Luis, null, Civil(Today.AddDays(-1))),
            NewTask(TenantId, "Due today", Ana, _bossClient, Civil(Today)),
            NewTask(TenantId, "Timed this morning", Ana, null, Timed(Now.AddHours(-2))),
            NewTask(TenantId, "Soon", null, _bossClient, Civil(Today.AddDays(3))),
            NewTask(TenantId, "Statutory late", Ana, _otherClient, Civil(Today.AddDays(-5), statutory: true)),
            NewTask(TenantId, "Form 941", null, _bossClient, Civil(Today.AddDays(20), statutory: true)),
            NewTask(TenantId, "FORM 941", Ana, _otherClient, Civil(Today.AddDays(20), statutory: true)),
            NewTask(TenantId, "Statutory beyond", Ana, _otherClient, Civil(Today.AddDays(40), statutory: true)),
            NewTask(TenantId, "Pool today", null, null, Civil(Today)),
            waiting,
            doneToday,
            doneYesterday,
            NewTask(Guid.NewGuid(), "Other tenant", Ana, _bossClient, Civil(Today.AddDays(-4)))
        );
        db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(TenantId, _bossClient, Boss, Now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task AssertAsync(ITaskSummaryReader reader)
    {
        var office = await reader.ReadAsync(Filter(null), default);
        var boss = await reader.ReadAsync(Filter(Boss), default);

        Assert.Equal(Today, office.Today);
        Assert.Equal((11, new TaskStatusCounts(10, 0, 1)), (office.Open, office.ByStatus));
        Assert.Equal(
            (3, 3, 3, 1, 1),
            (office.Overdue, office.DueToday, office.DueSoon, office.StatutoryOverdue, office.CompletedToday)
        );
        Assert.Null(office.OverdueAssigneeUserId);
        Assert.Equal(TaskSummaryLimits.TopItems, office.Top.Count);
        Assert.Equal(
            ["Statutory late", "Late", "Luis late", "Timed this morning"],
            office.Top.Take(4).Select(t => t.Title).ToArray()
        );
        Assert.Equal(Today.AddDays(-5), office.Top[0].DueOn);
        Assert.True(office.Top[0].DueIsStatutory);
        Assert.Equal(2, office.Deadlines.Count);
        Assert.Equal(new TaskDeadlineGroup(Today.AddDays(-5), "Statutory late", 1, 1), office.Deadlines[0]);
        Assert.Equal(
            (Today.AddDays(20), 2, 2),
            (office.Deadlines[1].DueOn, office.Deadlines[1].ClientCount, office.Deadlines[1].TaskCount)
        );
        Assert.Equal("form 941", office.Deadlines[1].Title.ToLowerInvariant());
        Assert.False(office.DeadlinesTruncated);

        Assert.Equal((5, new TaskStatusCounts(5, 0, 0)), (boss.Open, boss.ByStatus));
        Assert.Equal(
            (1, 2, 3, 0, 1),
            (boss.Overdue, boss.DueToday, boss.DueSoon, boss.StatutoryOverdue, boss.CompletedToday)
        );
        Assert.Equal(Ana, boss.OverdueAssigneeUserId);
        Assert.Equal("Late", boss.Top[0].Title);
        Assert.Equal(["Due today", "Pool today"], boss.Top.Skip(1).Select(t => t.Title).Order().ToArray());
        Assert.Equal(new TaskDeadlineGroup(Today.AddDays(20), "Form 941", 1, 1), Assert.Single(boss.Deadlines));
    }

    private static DueDate Civil(DateOnly day, bool statutory = false) =>
        DueDate.EndOfCivilDay(day, NewYork, statutory).Value;

    private static DueDate Timed(DateTime dueAtUtc) => DueDate.Create(dueAtUtc, "America/New_York", false).Value;

    private TaskItem NewTask(Guid tenantId, string title, Guid? assignee, Guid? customerId, DueDate? due) =>
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
                Now.AddDays(-20)
            )
            .Value;
}
