using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Web.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>"Waiting on clients" de toda la oficina: exige users.work.view y cuenta con el ABAC de quien mira.</summary>
public sealed class GetOfficeWaitingHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Without_users_work_view_is_forbidden()
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(canViewOffice: false, canViewAll: true, reader);

        Assert.True(result.IsFailure);
        Assert.Equal(403, result.Error.ToHttpStatusCode());
        Assert.False(reader.WasCalled);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Counts_keep_the_viewer_cut(bool canViewAll, bool visibilityOn, bool restricted)
    {
        var reader = new RecordingTaskWorkReader();

        var result = await HandleAsync(canViewOffice: true, canViewAll, reader, visibilityOn);

        Assert.True(result.IsSuccess);
        Assert.Equal(restricted ? Actor : null, reader.OfficeVisibleTo);
        Assert.Equal([WorkKinds.Tasks, WorkKinds.ClientRequests], result.Value.Select(c => c.Kind));
    }

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<OfficeWaitingCount>>> HandleAsync(
        bool canViewOffice,
        bool canViewAll,
        RecordingTaskWorkReader reader,
        bool visibilityOn = true
    ) =>
        GetOfficeWaitingHandler.Handle(
            new GetOfficeWaitingQuery(TenantId, Actor, canViewOffice, canViewAll),
            reader,
            Options.Create(new TasksVisibilityOptions { Enabled = visibilityOn }),
            CancellationToken.None
        );
}
