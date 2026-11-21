using BuildingBlocks.Contracts.Work;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Un archivo de tarea es visible si sigue adjunto y disponible en al menos una tarea que quien mira ve en el drawer
/// (<c>TaskReadVisibility</c>), traducido por SQL Server.
/// </summary>
public sealed class TaskFileVisibilityReaderTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _client = Guid.NewGuid();

    private readonly Guid _available = Guid.NewGuid();
    private readonly Guid _hidden = Guid.NewGuid();
    private readonly Guid _shared = Guid.NewGuid();
    private readonly Guid _pending = Guid.NewGuid();
    private readonly Guid _detached = Guid.NewGuid();
    private readonly Guid _rejected = Guid.NewGuid();
    private readonly Guid _linked = Guid.NewGuid();
    private readonly Guid _foreign = Guid.NewGuid();

    private TaskItem _mine = null!;
    private TaskItem _theirs = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _mine = NewTask(_tenantId, _me, "Prepare return", _client, Now);
        _theirs = NewTask(_tenantId, _other, "Someone else's", Guid.NewGuid(), Now.AddMinutes(5));
        var foreign = NewTask(Guid.NewGuid(), _me, "Other office", null, Now);

        Available(_mine, _available);
        Available(_theirs, _hidden);
        Available(_mine, _shared);
        Available(_theirs, _shared);
        Upload(_mine, _pending);
        Available(_mine, _detached);
        _mine.MarkAttachmentDetached(_detached, Now);
        Upload(_mine, _rejected);
        _mine.MarkAttachmentRejected(_rejected, "infected", Now);
        _mine.LinkExistingFile(_linked, "W-2.pdf", "application/pdf", 10, _me, Now);
        Available(foreign, _foreign);

        await using var db = _database.NewContext(Guid.Empty);
        db.Tasks.AddRange(_mine, _theirs, foreign);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_restricted_viewer_only_gets_available_files_of_tasks_he_can_see()
    {
        var files = await ReadAsync(_me);

        Assert.Equal(new[] { _available, _shared, _linked }.Order(), files.Select(f => f.FileId).Order());
        var available = files.Single(f => f.FileId == _available);
        Assert.Equal(
            (WorkResourceTypes.Task, _mine.Id, "Prepare return", (Guid?)_client),
            (available.ResourceType, available.ResourceId, available.Title, available.CustomerId)
        );
    }

    [Fact]
    public async Task A_file_in_two_tasks_points_to_the_visible_one()
    {
        var files = await ReadAsync(_me);

        Assert.Equal(_mine.Id, files.Single(f => f.FileId == _shared).ResourceId);
    }

    [Fact]
    public async Task Without_restriction_every_task_counts_and_the_newest_reference_wins()
    {
        var files = await ReadAsync(null);

        Assert.Equal(new[] { _available, _hidden, _shared, _linked }.Order(), files.Select(f => f.FileId).Order());
        Assert.Equal(_theirs.Id, files.Single(f => f.FileId == _shared).ResourceId);
        Assert.DoesNotContain(files, f => f.FileId == _foreign);
    }

    private async Task<IReadOnlyList<BuildingBlocks.Contracts.Files.VisibleFile>> ReadAsync(Guid? visibleTo)
    {
        await using var db = _database.NewContext(Guid.Empty);
        return await new TaskFileVisibilityReader(db).VisibleTaskFilesAsync(
            _tenantId,
            [_available, _hidden, _shared, _pending, _detached, _rejected, _linked, _foreign, Guid.NewGuid()],
            visibleTo,
            default
        );
    }

    private void Available(TaskItem task, Guid fileId)
    {
        Upload(task, fileId);
        task.MarkAttachmentAvailable(fileId);
    }

    private static void Upload(TaskItem task, Guid fileId) =>
        Assert.True(
            task.AttachUploadedFile(
                fileId,
                "W-2.pdf",
                "application/pdf",
                10,
                task.CreatedByUserId,
                task.CreatedAtUtc
            ).IsSuccess
        );

    private static TaskItem NewTask(Guid tenantId, Guid owner, string title, Guid? customerId, DateTime nowUtc) =>
        TaskItem
            .Create(
                tenantId,
                owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                customerId is null ? TaskReference.None : TaskReference.Create(customerId, 2025).Value,
                null,
                null,
                owner,
                nowUtc
            )
            .Value;
}
