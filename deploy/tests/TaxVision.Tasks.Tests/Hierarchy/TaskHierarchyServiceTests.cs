using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Hierarchy;

public sealed class TaskHierarchyServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Closing_a_child_lowers_the_parent_count()
    {
        var parent = NewRoot();
        var child = NewSubtask(parent);
        var (service, _) = Build(parent, child);

        await service.ApplyChildClosedAsync(TenantId, child.ParentTaskId, CancellationToken.None);

        Assert.Equal(0, parent.OpenSubtaskCount);
    }

    [Fact]
    public async Task Reopening_a_child_raises_it_again()
    {
        var parent = NewRoot();
        var child = NewSubtask(parent);
        var (service, _) = Build(parent, child);
        await service.ApplyChildClosedAsync(TenantId, child.ParentTaskId, CancellationToken.None);

        await service.ApplyChildReopenedAsync(TenantId, child.ParentTaskId, CancellationToken.None);

        Assert.Equal(1, parent.OpenSubtaskCount);
    }

    [Fact]
    public async Task A_root_task_has_no_parent_to_touch()
    {
        var root = NewRoot();
        var (service, _) = Build(root);

        await service.ApplyChildClosedAsync(TenantId, root.ParentTaskId, CancellationToken.None);

        Assert.Equal(0, root.OpenSubtaskCount);
    }

    [Fact]
    public async Task Deleting_a_parent_takes_the_whole_subtree()
    {
        var root = NewRoot();
        var child = NewSubtask(root);
        var grandchild = NewSubtask(child);
        var (service, repository) = Build(root, child, grandchild);

        await service.DeleteWithDescendantsAsync(TenantId, root.Id, UserId, CancellationToken.None);

        var survivors = await repository.ListByIdsAsync(
            TenantId,
            [root.Id, child.Id, grandchild.Id],
            CancellationToken.None
        );
        Assert.Empty(survivors);
    }

    [Fact]
    public async Task Deleting_a_child_leaves_its_siblings_and_frees_the_parent_count()
    {
        var root = NewRoot();
        var first = NewSubtask(root);
        var second = NewSubtask(root);
        var (service, repository) = Build(root, first, second);

        await service.DeleteWithDescendantsAsync(TenantId, first.Id, UserId, CancellationToken.None);

        var survivors = await repository.ListByIdsAsync(TenantId, [root.Id, second.Id], CancellationToken.None);
        Assert.Equal(2, survivors.Count);
        Assert.Equal(1, root.OpenSubtaskCount);
    }

    private static (TaskHierarchyService Service, InMemoryTaskRepository Repository) Build(params TaskItem[] tasks)
    {
        var repository = new InMemoryTaskRepository(tasks);
        return (new TaskHierarchyService(repository), repository);
    }

    private static TaskItem NewRoot() =>
        TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                UserId,
                Now
            )
            .Value;

    private static TaskItem NewSubtask(TaskItem parent) =>
        TaskItem
            .CreateSubtask(
                parent,
                UserId,
                TaskTitle.Create("Juntar los W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                UserId,
                Now
            )
            .Value;
}
