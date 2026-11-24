using TaxVision.Tasks.Application.Statuses;
using TaxVision.Tasks.Application.Statuses.Commands;
using TaxVision.Tasks.Application.Statuses.Queries;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Labels;

/// <summary>Catálogo de estados de la oficina: alta, edición, por defecto, archivado y lista.</summary>
public sealed class WorkflowStatusHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Creating_generates_the_code_and_avoids_collisions()
    {
        var repo = new InMemoryWorkflowStatusRepository(Status("Under review", TaskItemStatus.InProgress));

        var created = await Create(repo, "under REVIEW", TaskItemStatus.NotStarted);

        Assert.Equal(TaskErrors.Status.NameTaken, created.Error);
        var other = await Create(repo, "Under-review!", TaskItemStatus.NotStarted);
        Assert.Equal("under_review_2", other.Value.Code);
    }

    [Fact]
    public async Task The_same_name_is_fine_in_another_office()
    {
        var repo = new InMemoryWorkflowStatusRepository(Status("Under review", TaskItemStatus.InProgress, OtherTenant));

        var created = await Create(repo, "Under review", TaskItemStatus.InProgress);

        Assert.True(created.IsSuccess);
    }

    [Fact]
    public async Task Completed_and_cancelled_do_not_take_new_statuses()
    {
        var created = await Create(new InMemoryWorkflowStatusRepository(), "Filed", TaskItemStatus.Completed);

        Assert.Equal(TaskErrors.Status.CategoryLocked, created.Error);
    }

    [Fact]
    public async Task An_office_has_a_limit_of_active_statuses()
    {
        var full = Enumerable
            .Range(0, WorkflowStatus.MaxActivePerTenant)
            .Select(i => Status($"Status {i}", TaskItemStatus.InProgress))
            .ToArray();

        var created = await Create(new InMemoryWorkflowStatusRepository(full), "One more", TaskItemStatus.InProgress);

        Assert.Equal(TaskErrors.Status.LimitReached, created.Error);
    }

    [Fact]
    public async Task Editing_checks_the_version_and_a_repeated_name()
    {
        var mine = Status("Under review", TaskItemStatus.InProgress);
        var repo = new InMemoryWorkflowStatusRepository(mine, Status("Ready", TaskItemStatus.InProgress));

        var taken = await Update(repo, "under_review", "Ready", "AAAAAAAAB9E=");
        var renamed = await Update(repo, "under_review", "Partner review", "AAAAAAAAB9E=");

        Assert.Equal(TaskErrors.Status.NameTaken, taken.Error);
        Assert.Equal("Partner review", renamed.Value.DisplayName);
    }

    [Fact]
    public async Task Customizing_a_system_status_creates_its_row_as_the_default()
    {
        var repo = new InMemoryWorkflowStatusRepository();

        var updated = await Update(repo, "completed", "Done", null);

        Assert.True(updated.Value.IsSystem);
        Assert.True(updated.Value.IsDefault);
        Assert.Equal("Done", Assert.Single(repo.Stored).DisplayName);
    }

    [Fact]
    public async Task The_list_adds_the_system_statuses_without_a_row()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        review.MakeDefault();
        var repo = new InMemoryWorkflowStatusRepository(review);

        var list = await ListWorkflowStatusesHandler.Handle(
            new ListWorkflowStatusesQuery(TenantId, false),
            repo,
            default
        );

        Assert.Equal(6, list.Statuses.Count);
        Assert.False(list.Statuses.Single(s => s.Code == "in_progress").IsDefault);
        Assert.True(list.Statuses.Single(s => s.Code == "under_review").IsDefault);
        Assert.True(list.Statuses.Single(s => s.Code == "not_started").IsDefault);
    }

    [Fact]
    public async Task The_list_counts_open_tasks_and_gives_the_ones_without_status_to_the_default()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        var repo = new InMemoryWorkflowStatusRepository(review);
        repo.Tasks.Add(TaskIn(review));
        repo.Tasks.Add(TaskIn(review));
        repo.Tasks.Add(PlainTask());
        var closed = TaskIn(review);
        closed.Complete(Admin, Now);
        repo.Tasks.Add(closed);

        var list = await ListWorkflowStatusesHandler.Handle(
            new ListWorkflowStatusesQuery(TenantId, false),
            repo,
            default
        );

        Assert.Equal(2, list.Statuses.Single(s => s.Code == "under_review").OpenTaskCount);
        Assert.Equal(1, list.Statuses.Single(s => s.Code == "not_started").OpenTaskCount);
        Assert.Equal(0, list.Statuses.Single(s => s.Code == "in_progress").OpenTaskCount);
    }

    [Fact]
    public async Task Archiving_with_open_tasks_asks_where_to_move_them()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        var repo = new InMemoryWorkflowStatusRepository(review);
        repo.Tasks.Add(TaskIn(review));

        var archived = await Archive(repo, "under_review", null);

        Assert.Equal("Task.Status.InUse", archived.Error.Code);
        Assert.False(review.IsArchived);
    }

    [Fact]
    public async Task Archiving_moves_only_the_open_tasks_and_can_repeat()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        var ready = Status("Ready to file", TaskItemStatus.InProgress);
        var repo = new InMemoryWorkflowStatusRepository(review, ready);
        var open = TaskIn(review);
        repo.Tasks.Add(open);

        var archived = await Archive(repo, "under_review", "ready_to_file");
        var again = await Archive(repo, "under_review", "ready_to_file");

        Assert.True(archived.IsSuccess);
        Assert.True(again.IsSuccess);
        Assert.True(review.IsArchived);
        Assert.Equal(ready.Id, open.WorkflowStatusId);
    }

    [Fact]
    public async Task Archiving_cannot_move_tasks_to_another_stage()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        var repo = new InMemoryWorkflowStatusRepository(review);
        repo.Tasks.Add(TaskIn(review));

        var archived = await Archive(repo, "under_review", "not_started");

        Assert.Equal(TaskErrors.Status.CategoryLocked, archived.Error);
    }

    [Fact]
    public async Task Choosing_a_new_default_takes_it_from_the_previous_one()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        var ready = Status("Ready to file", TaskItemStatus.InProgress);
        review.MakeDefault();
        var repo = new InMemoryWorkflowStatusRepository(review, ready);

        var result = await SetDefaultWorkflowStatusHandler.Handle(
            new SetDefaultWorkflowStatusCommand(TenantId, Admin, "ready_to_file"),
            repo,
            new RecordingUnitOfWork(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.False(review.IsDefault);
        Assert.True(ready.IsDefault);
    }

    private static Task<BuildingBlocks.Results.Result<WorkflowStatusResponse>> Create(
        InMemoryWorkflowStatusRepository repo,
        string name,
        TaskItemStatus category
    ) =>
        CreateWorkflowStatusHandler.Handle(
            new CreateWorkflowStatusCommand(TenantId, Admin, name, "#2563EB", category, null),
            repo,
            new RecordingUnitOfWork(),
            default
        );

    private static Task<BuildingBlocks.Results.Result<WorkflowStatusResponse>> Update(
        InMemoryWorkflowStatusRepository repo,
        string code,
        string name,
        string? rowVersion
    ) =>
        UpdateWorkflowStatusHandler.Handle(
            new UpdateWorkflowStatusCommand(TenantId, Admin, code, name, "#2563EB", null, rowVersion),
            repo,
            new RecordingUnitOfWork(),
            default
        );

    private static Task<BuildingBlocks.Results.Result> Archive(
        InMemoryWorkflowStatusRepository repo,
        string code,
        string? target
    ) =>
        ArchiveWorkflowStatusHandler.Handle(
            new ArchiveWorkflowStatusCommand(TenantId, Admin, code, target),
            repo,
            new RecordingUnitOfWork(),
            default
        );

    private static WorkflowStatus Status(string name, TaskItemStatus category, Guid? tenantId = null) =>
        WorkflowStatus
            .CreateCustom(
                tenantId ?? TenantId,
                TaskLabelCode.FromDisplayName(name, new HashSet<string>()),
                name,
                LabelColor.Create("#2563EB").Value,
                category,
                10,
                null,
                Admin,
                Now
            )
            .Value;

    private static TaskItem PlainTask() =>
        TaskItem
            .Create(
                TenantId,
                Admin,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Admin,
                Now
            )
            .Value;

    private static TaskItem TaskIn(WorkflowStatus status)
    {
        var task = TaskItem
            .Create(
                TenantId,
                Admin,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Admin,
                Now
            )
            .Value;
        task.MoveTo(status.ToRef(), TaskMoveRequest.None, Admin, Now);
        return task;
    }
}
