using BuildingBlocks.Web.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Alcance "Me / Office" del resumen: la oficina exige users.work.view y nunca suelta el recorte por asignación;
/// "Me" filtra por el actor; sin alcance, igual que siempre.
/// </summary>
public sealed class GetTaskSummaryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);

    private readonly RecordingSummaryReader _reader = new();

    [Fact]
    public async Task Office_without_users_work_view_is_forbidden()
    {
        var result = await HandleAsync(DashboardScope.Office, canViewOffice: false, canViewAll: true);

        Assert.True(result.IsFailure);
        Assert.Equal(403, result.Error.ToHttpStatusCode());
        Assert.Null(_reader.Last);
    }

    [Fact]
    public async Task Office_drops_the_assignee_and_keeps_the_actor_cut_without_view_all()
    {
        var result = await HandleAsync(DashboardScope.Office, canViewOffice: true, canViewAll: false, Colleague);

        Assert.True(result.IsSuccess);
        Assert.Null(_reader.Last!.AssigneeUserId);
        Assert.Equal(Actor, _reader.Last.VisibleTo);
    }

    [Fact]
    public async Task Office_with_view_all_reads_the_whole_office()
    {
        await HandleAsync(DashboardScope.Office, canViewOffice: true, canViewAll: true);

        Assert.Null(_reader.Last!.AssigneeUserId);
        Assert.Null(_reader.Last.VisibleTo);
    }

    [Fact]
    public async Task Me_counts_the_actor_assignments_without_extra_permission()
    {
        var result = await HandleAsync(DashboardScope.Me, canViewOffice: false, canViewAll: false, Colleague);

        Assert.True(result.IsSuccess);
        Assert.Equal(Actor, _reader.Last!.AssigneeUserId);
        Assert.Equal(Actor, _reader.Last.VisibleTo);
    }

    [Fact]
    public async Task Without_scope_the_filters_pass_through_as_before()
    {
        var result = await HandleAsync(DashboardScope.Default, canViewOffice: false, canViewAll: false, Colleague);

        Assert.True(result.IsSuccess);
        Assert.Equal(Colleague, _reader.Last!.AssigneeUserId);
        Assert.Equal(Actor, _reader.Last.VisibleTo);
    }

    private Task<BuildingBlocks.Results.Result<TaskSummaryResponse>> HandleAsync(
        DashboardScope scope,
        bool canViewOffice,
        bool canViewAll,
        Guid? assignee = null
    ) =>
        GetTaskSummaryHandler.Handle(
            new GetTaskSummaryQuery(TenantId, Actor, canViewAll, assignee, null, null, Now, scope, canViewOffice),
            _reader,
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

    private sealed class RecordingSummaryReader : ITaskSummaryReader
    {
        public TaskSummaryFilter? Last { get; private set; }

        public Task<TaskSummaryResponse> ReadAsync(TaskSummaryFilter filter, CancellationToken ct)
        {
            Last = filter;
            return Task.FromResult(
                new TaskSummaryResponse(
                    filter.Window.Today,
                    0,
                    new TaskStatusCounts(0, 0, 0),
                    0,
                    0,
                    0,
                    0,
                    0,
                    null,
                    [],
                    [],
                    false
                )
            );
        }
    }
}
