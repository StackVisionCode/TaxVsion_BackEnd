using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// La lista que abre cada número del dashboard (`/task?due=`): mismo total que <c>GET /tasks/summary</c> con el mismo
/// alcance, porque el corte del vencimiento sale del mismo predicado.
/// </summary>
public sealed class SearchTasksHandlerTests
{
    [Fact]
    public async Task Due_filters_list_exactly_what_the_summary_counts()
    {
        await using var db = NewContext();
        var scenario = new TaskSummaryScenario();
        await scenario.SeedAsync(db);

        await AssertMatchesSummaryAsync(db, scenario, null);
    }

    [Fact]
    public async Task Due_filters_follow_the_viewer_scope_like_the_summary()
    {
        await using var db = NewContext();
        var scenario = new TaskSummaryScenario();
        await scenario.SeedAsync(db);

        await AssertMatchesSummaryAsync(db, scenario, scenario.Boss);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Statutory_filter_lists_exactly_the_filing_deadlines_count(bool restricted)
    {
        await using var db = NewContext();
        var scenario = new TaskSummaryScenario();
        await scenario.SeedAsync(db);

        await AssertStatutoryMatchesDeadlinesAsync(db, scenario, restricted ? scenario.Boss : null);
    }

    [Fact]
    public async Task Due_filter_combines_with_assignee_and_status()
    {
        await using var db = NewContext();
        var scenario = new TaskSummaryScenario();
        await scenario.SeedAsync(db);

        var anaOverdue = await SearchAsync(db, scenario, null, TaskDueBucket.Overdue, scenario.Ana);
        var notStartedToday = await SearchAsync(
            db,
            scenario,
            null,
            TaskDueBucket.Today,
            status: TaskItemStatus.NotStarted
        );

        Assert.Equal(2, anaOverdue.TotalCount);
        Assert.All(anaOverdue.Items, task => Assert.Equal(scenario.Ana, task.AssigneeUserId));
        Assert.Equal(3, notStartedToday.TotalCount);
    }

    // Shared con la prueba de integración: el total de cada lista contra el número del resumen.
    internal static async Task AssertMatchesSummaryAsync(TasksDbContext db, TaskSummaryScenario scenario, Guid? viewer)
    {
        var summary = await new TaskSummaryReader(db).ReadAsync(scenario.Filter(viewer), default);

        var overdue = await SearchAsync(db, scenario, viewer, TaskDueBucket.Overdue);
        var today = await SearchAsync(db, scenario, viewer, TaskDueBucket.Today);
        var soon = await SearchAsync(db, scenario, viewer, TaskDueBucket.Soon);

        Assert.Equal(
            (summary.Overdue, summary.DueToday, summary.DueSoon),
            (overdue.TotalCount, today.TotalCount, soon.TotalCount)
        );
        Assert.Equal(viewer is null ? (3, 3, 3) : (1, 2, 3), (overdue.TotalCount, today.TotalCount, soon.TotalCount));
    }

    // El número de Filing deadlines es la suma de tareas de sus grupos (vencidas y del horizonte).
    internal static async Task AssertStatutoryMatchesDeadlinesAsync(
        TasksDbContext db,
        TaskSummaryScenario scenario,
        Guid? viewer
    )
    {
        var summary = await new TaskSummaryReader(db).ReadAsync(scenario.Filter(viewer), default);

        var statutory = await SearchAsync(db, scenario, viewer, TaskDueBucket.Statutory);

        Assert.False(summary.DeadlinesTruncated);
        Assert.Equal(summary.Deadlines.Sum(g => g.TaskCount), statutory.TotalCount);
        Assert.Equal(viewer is null ? 3 : 1, statutory.TotalCount);
        Assert.Equal(
            viewer is null ? ["FORM 941", "Form 941", "Statutory late"] : ["Form 941"],
            statutory.Items.Select(t => t.Title).Order(StringComparer.Ordinal)
        );
    }

    private static Task<BuildingBlocks.Common.PagedResult<TaxVision.Tasks.Application.Tasks.TaskResponse>> SearchAsync(
        TasksDbContext db,
        TaskSummaryScenario scenario,
        Guid? viewer,
        TaskDueBucket due,
        Guid? assignee = null,
        TaskItemStatus? status = null
    ) =>
        SearchTasksHandler.Handle(
            new SearchTasksQuery(
                scenario.TenantId,
                new TaskQueryFilter(Status: status, AssigneeUserId: assignee),
                1,
                100,
                viewer ?? Guid.NewGuid(),
                CanViewAll: viewer is null,
                due,
                TaskSummaryScenario.Window.Work.NowUtc
            ),
            new TaskRepository(db),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            default
        );

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
