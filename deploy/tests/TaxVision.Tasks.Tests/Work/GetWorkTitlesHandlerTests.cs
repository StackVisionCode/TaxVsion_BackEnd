using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>Títulos en vivo: el ABAC del que mira llega al lector, se recortan y lo que no tiene título se omite.</summary>
public sealed class GetWorkTitlesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Titles_are_read_with_the_viewer_ABAC(bool canViewAll, bool visibilityOn, bool restricted)
    {
        var reader = new RecordingTaskWorkReader();

        await HandleAsync(reader, [Guid.NewGuid()], [], canViewAll, visibilityOn);

        Assert.Equal(restricted ? Actor : null, reader.TitlesVisibleTo);
    }

    [Fact]
    public async Task Ids_reach_the_reader_by_type()
    {
        var reader = new RecordingTaskWorkReader();
        Guid task = Guid.NewGuid(),
            request = Guid.NewGuid();

        await HandleAsync(reader, [task], [request]);

        Assert.Equal([task], reader.TitleTaskIds);
        Assert.Equal([request], reader.TitleRequestIds);
    }

    [Fact]
    public async Task Titles_are_clipped_and_blank_ones_are_left_out()
    {
        Guid task = Guid.NewGuid(),
            blank = Guid.NewGuid(),
            longOne = Guid.NewGuid();
        var reader = new RecordingTaskWorkReader
        {
            Titles =
            [
                new WorkTitle(WorkResourceTypes.Task, task, "  Review \n W-2  "),
                new WorkTitle(WorkResourceTypes.Task, blank, "   "),
                new WorkTitle(WorkResourceTypes.ClientRequest, longOne, new string('a', 200)),
            ],
        };

        var titles = await HandleAsync(reader, [task, blank], [longOne]);

        Assert.Equal([task, longOne], titles.Select(t => t.Id));
        Assert.Equal("Review W-2", titles[0].Title);
        Assert.Equal(WorkLimits.TitleMaxLength, titles[1].Title.Length);
        Assert.EndsWith("…", titles[1].Title);
    }

    [Fact]
    public async Task Nothing_asked_reads_nothing()
    {
        var reader = new RecordingTaskWorkReader();

        var titles = await HandleAsync(reader, [], []);

        Assert.Empty(titles);
        Assert.False(reader.WasCalled);
    }

    private static Task<IReadOnlyList<WorkTitle>> HandleAsync(
        RecordingTaskWorkReader reader,
        IReadOnlyList<Guid> taskIds,
        IReadOnlyList<Guid> requestIds,
        bool canViewAll = false,
        bool visibilityOn = true
    ) =>
        GetWorkTitlesHandler.Handle(
            new GetWorkTitlesQuery(TenantId, Actor, canViewAll, taskIds, requestIds),
            reader,
            Options.Create(new TasksVisibilityOptions { Enabled = visibilityOn }),
            CancellationToken.None
        );
}
