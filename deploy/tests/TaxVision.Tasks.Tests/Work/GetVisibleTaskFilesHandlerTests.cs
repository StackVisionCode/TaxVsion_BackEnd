using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Files;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>Archivos visibles de tareas: el ABAC del que mira llega al lector y una lista vacía no consulta.</summary>
public sealed class GetVisibleTaskFilesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Files_are_read_with_the_viewer_ABAC(bool canViewAll, bool visibilityOn, bool restricted)
    {
        var reader = new RecordingReader();
        var fileId = Guid.NewGuid();

        await HandleAsync(reader, [fileId], canViewAll, visibilityOn);

        Assert.Equal(restricted ? Actor : null, reader.VisibleTo);
        Assert.Equal(TenantId, reader.TenantId);
        Assert.Equal([fileId], reader.FileIds!);
    }

    [Fact]
    public async Task Titles_are_clipped()
    {
        var fileId = Guid.NewGuid();
        var reader = new RecordingReader
        {
            Files = [new VisibleFile(fileId, WorkResourceTypes.Task, Guid.NewGuid(), new string('a', 200), null)],
        };

        var files = await HandleAsync(reader, [fileId]);

        Assert.Equal(WorkLimits.TitleMaxLength, Assert.Single(files).Title!.Length);
    }

    [Fact]
    public async Task Nothing_asked_reads_nothing()
    {
        var reader = new RecordingReader();

        var files = await HandleAsync(reader, []);

        Assert.Empty(files);
        Assert.False(reader.WasCalled);
    }

    private static Task<IReadOnlyList<VisibleFile>> HandleAsync(
        RecordingReader reader,
        IReadOnlyList<Guid> fileIds,
        bool canViewAll = false,
        bool visibilityOn = true
    ) =>
        GetVisibleTaskFilesHandler.Handle(
            new GetVisibleTaskFilesQuery(TenantId, Actor, canViewAll, fileIds),
            reader,
            Options.Create(new TasksVisibilityOptions { Enabled = visibilityOn }),
            CancellationToken.None
        );

    private sealed class RecordingReader : ITaskFileVisibilityReader
    {
        public IReadOnlyList<VisibleFile> Files { get; init; } = [];
        public bool WasCalled { get; private set; }
        public Guid TenantId { get; private set; }
        public IReadOnlyCollection<Guid>? FileIds { get; private set; }
        public Guid? VisibleTo { get; private set; }

        public Task<IReadOnlyList<VisibleFile>> VisibleTaskFilesAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            Guid? visibleTo,
            CancellationToken ct
        )
        {
            WasCalled = true;
            (TenantId, FileIds, VisibleTo) = (tenantId, fileIds, visibleTo);
            return Task.FromResult(Files);
        }
    }
}
