using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>Lista paginada de un tipo: misma autorización que el resumen y el ABAC del que mira.</summary>
public sealed class GetWorkItemsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();

    [Fact]
    public async Task Someone_else_without_users_work_view_is_forbidden()
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(reader, Colleague, WorkKinds.Tasks, canViewOthers: false);

        Assert.Equal(403, result.Error.ToHttpStatusCode());
        Assert.False(reader.WasCalled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("appointments")]
    [InlineData("form-requests")]
    [InlineData("Tasks")]
    public async Task A_kind_this_handler_does_not_serve_is_a_bad_request(string? kind)
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(reader, Actor, kind, canViewOthers: false);

        Assert.Equal(400, result.Error.ToHttpStatusCode());
        Assert.False(reader.WasCalled);
    }

    [Theory]
    [InlineData(WorkKinds.Tasks)]
    [InlineData(WorkKinds.ClientRequests)]
    public async Task Items_use_the_kind_the_page_and_the_viewer_ABAC(string kind)
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(reader, Colleague, kind, canViewOthers: true, page: 3, size: 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            (Colleague, kind, 3, 10, (Guid?)Actor),
            (reader.UserId!.Value, reader.ItemsKind!, reader.ItemsPage, reader.ItemsSize, reader.ItemsVisibleTo)
        );
    }

    private static Task<BuildingBlocks.Results.Result<BuildingBlocks.Common.PagedResult<WorkItem>>> HandleAsync(
        RecordingTaskWorkReader reader,
        Guid userId,
        string? kind,
        bool canViewOthers,
        int page = 1,
        int size = WorkLimits.ItemsPageSize
    ) =>
        GetWorkItemsHandler.Handle(
            new GetWorkItemsQuery(TenantId, Actor, userId, canViewOthers, false, kind, page, size),
            reader,
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );
}
