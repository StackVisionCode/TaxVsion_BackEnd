using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>El lote siempre exige users.work.view, acota la cantidad y cuenta con el ABAC del que mira.</summary>
public sealed class GetWorkBatchHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Without_users_work_view_is_forbidden_even_for_oneself()
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync([Actor], canViewOthers: false, canViewAll: true, reader);

        Assert.Equal(403, result.Error.ToHttpStatusCode());
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task More_users_than_the_limit_is_a_bad_request()
    {
        var reader = new RecordingTaskWorkReader();
        var users = Enumerable.Range(0, WorkLimits.BatchUsers + 1).Select(_ => Guid.NewGuid()).ToList();

        var result = await HandleAsync(users, canViewOthers: true, canViewAll: true, reader);

        Assert.Equal(400, result.Error.ToHttpStatusCode());
        Assert.False(reader.WasCalled);
    }

    [Fact]
    public async Task Counts_with_the_viewer_ABAC_and_distinct_users()
    {
        var colleague = Guid.NewGuid();
        var reader = new RecordingTaskWorkReader { BatchRows = [new WorkBatchRow(colleague, 3, 1)] };

        var result = await HandleAsync([colleague, colleague, Actor], canViewOthers: true, canViewAll: false, reader);

        Assert.True(result.IsSuccess);
        Assert.Equal([colleague, Actor], reader.BatchUserIds!.ToArray());
        Assert.Equal(Actor, reader.CountVisibleTo);
        Assert.Equal(new WorkBatchRow(colleague, 3, 1), Assert.Single(result.Value));
    }

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<WorkBatchRow>>> HandleAsync(
        IReadOnlyList<Guid> userIds,
        bool canViewOthers,
        bool canViewAll,
        RecordingTaskWorkReader reader
    ) =>
        GetWorkBatchHandler.Handle(
            new GetWorkBatchQuery(TenantId, Actor, userIds, canViewOthers, canViewAll, Now),
            reader,
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );
}
