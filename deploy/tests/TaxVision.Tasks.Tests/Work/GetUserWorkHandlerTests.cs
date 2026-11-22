using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>El propio usuario siempre; otra persona solo con users.work.view; el ABAC del que mira llega al lector.</summary>
public sealed class GetUserWorkHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Own_work_needs_no_extra_permission()
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(Actor, canViewOthers: false, canViewAll: false, reader);

        Assert.True(result.IsSuccess);
        Assert.Equal(Actor, reader.UserId);
    }

    [Fact]
    public async Task Someone_else_without_users_work_view_is_forbidden()
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(Colleague, canViewOthers: false, canViewAll: true, reader);

        Assert.True(result.IsFailure);
        Assert.Equal(403, result.Error.ToHttpStatusCode());
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task Someone_else_with_users_work_view_is_allowed()
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(Colleague, canViewOthers: true, canViewAll: false, reader);

        Assert.True(result.IsSuccess);
        Assert.Equal(Colleague, reader.UserId);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Counts_and_top_use_the_viewer_ABAC(bool canViewAll, bool visibilityOn, bool restricted)
    {
        var reader = new RecordingTaskWorkReader();

        await HandleAsync(Colleague, canViewOthers: true, canViewAll, reader, visibilityOn);

        Assert.Equal(restricted ? Actor : null, reader.CountVisibleTo);
        Assert.Equal(restricted ? Actor : null, reader.TopVisibleTo);
    }

    [Fact]
    public async Task The_window_is_today_in_the_office_zone()
    {
        var reader = new RecordingTaskWorkReader();

        await HandleAsync(Actor, canViewOthers: false, canViewAll: true, reader);

        // 03:00 UTC del 18 todavía es el 17 en Nueva York.
        Assert.Equal(new DateOnly(2026, 11, 17), reader.Window!.Today);
        Assert.Equal(WorkLimits.TopItems, reader.Take);
    }

    private static Task<BuildingBlocks.Results.Result<TaskWork>> HandleAsync(
        Guid userId,
        bool canViewOthers,
        bool canViewAll,
        RecordingTaskWorkReader reader,
        bool visibilityOn = true
    ) =>
        GetUserWorkHandler.Handle(
            new GetUserWorkQuery(TenantId, Actor, userId, canViewOthers, canViewAll, Now),
            reader,
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions { Enabled = visibilityOn }),
            CancellationToken.None
        );
}

internal sealed class RecordingTaskWorkReader : ITaskWorkReader
{
    public bool WasCalled { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? CountVisibleTo { get; private set; }
    public Guid? TopVisibleTo { get; private set; }
    public WorkWindow? Window { get; private set; }
    public int Take { get; private set; }
    public IReadOnlyCollection<Guid>? BatchUserIds { get; private set; }
    public IReadOnlyList<WorkBatchRow> BatchRows { get; init; } = [];

    public Task<IReadOnlyList<WorkCount>> CountAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        WorkWindow window,
        CancellationToken ct
    )
    {
        WasCalled = true;
        UserId = userId;
        CountVisibleTo = visibleTo;
        Window = window;
        return Task.FromResult<IReadOnlyList<WorkCount>>([new WorkCount(WorkKinds.Tasks, 1, 0, 0, 0)]);
    }

    public Task<IReadOnlyList<WorkItem>> TopAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        int take,
        CancellationToken ct
    )
    {
        WasCalled = true;
        TopVisibleTo = visibleTo;
        Take = take;
        return Task.FromResult<IReadOnlyList<WorkItem>>([]);
    }

    public Task<IReadOnlyList<WorkBatchRow>> CountByUserAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid? visibleTo,
        WorkWindow window,
        CancellationToken ct
    )
    {
        WasCalled = true;
        BatchUserIds = userIds;
        CountVisibleTo = visibleTo;
        return Task.FromResult(BatchRows);
    }

    public Guid? OfficeVisibleTo { get; private set; }

    public Task<IReadOnlyList<OfficeWaitingCount>> CountOfficeWaitingAsync(
        Guid tenantId,
        Guid? visibleTo,
        CancellationToken ct
    )
    {
        WasCalled = true;
        OfficeVisibleTo = visibleTo;
        return Task.FromResult<IReadOnlyList<OfficeWaitingCount>>([
            new OfficeWaitingCount(WorkKinds.Tasks, 2),
            new OfficeWaitingCount(WorkKinds.ClientRequests, 1),
        ]);
    }

    public IReadOnlyList<WorkTitle> Titles { get; init; } = [];
    public IReadOnlyCollection<Guid>? TitleTaskIds { get; private set; }
    public IReadOnlyCollection<Guid>? TitleRequestIds { get; private set; }
    public Guid? TitlesVisibleTo { get; private set; }

    public Task<IReadOnlyList<WorkTitle>> TitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        IReadOnlyCollection<Guid> clientRequestIds,
        Guid? visibleTo,
        CancellationToken ct
    )
    {
        WasCalled = true;
        TitleTaskIds = taskIds;
        TitleRequestIds = clientRequestIds;
        TitlesVisibleTo = visibleTo;
        return Task.FromResult(Titles);
    }

    public string? ItemsKind { get; private set; }
    public int ItemsPage { get; private set; }
    public int ItemsSize { get; private set; }
    public Guid? ItemsVisibleTo { get; private set; }

    public Task<BuildingBlocks.Common.PagedResult<WorkItem>> ItemsAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        string kind,
        int page,
        int size,
        CancellationToken ct
    )
    {
        WasCalled = true;
        UserId = userId;
        ItemsKind = kind;
        ItemsPage = page;
        ItemsSize = size;
        ItemsVisibleTo = visibleTo;
        return Task.FromResult(new BuildingBlocks.Common.PagedResult<WorkItem>([], page, size, 0));
    }
}
