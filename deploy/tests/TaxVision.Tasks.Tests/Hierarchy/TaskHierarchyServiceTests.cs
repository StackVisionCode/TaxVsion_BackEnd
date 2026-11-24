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
    public async Task Deleting_a_closed_subtask_keeps_the_open_count_of_its_siblings()
    {
        var parent = NewRoot();
        var done = NewSubtask(parent);
        NewSubtask(parent);
        done.Complete(UserId, Now);
        parent.RegisterSubtaskClosed();
        var (service, _) = Build(parent, done);

        await service.DeleteWithDescendantsAsync(TenantId, done.Id, UserId, CancellationToken.None);

        Assert.Equal(1, parent.OpenSubtaskCount);
    }

    [Fact]
    public async Task Deleting_a_subtask_lowers_the_parent_total()
    {
        var parent = NewRoot();
        var child = NewSubtask(parent);
        var (service, _) = Build(parent, child);

        await service.DeleteWithDescendantsAsync(TenantId, child.Id, UserId, CancellationToken.None);

        Assert.Equal(0, parent.SubtaskCount);
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
    public async Task Deleting_a_parent_takes_the_whole_subtree_to_the_trash()
    {
        var root = NewRoot();
        var child = NewSubtask(root);
        var grandchild = NewSubtask(child);
        var (service, _) = Build(root, child, grandchild);

        var branch = await service.DeleteWithDescendantsAsync(TenantId, root.Id, UserId, CancellationToken.None);

        Assert.Equal(3, branch.Count);
        Assert.All(new[] { root, child, grandchild }, t => Assert.Equal((true, root.Id), (t.IsInTrash, t.TrashRootId)));
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

    // Sin FK a Tasks: las aristas de una tarea borrada quedaban huérfanas y seguían saliendo en el grafo.
    [Fact]
    public async Task Deleting_a_task_removes_the_dependencies_of_its_whole_subtree()
    {
        var root = NewRoot();
        var child = NewSubtask(root);
        var other = NewRoot();
        var unrelated = NewRoot();
        var dependencies = new InMemoryTaskDependencyRepository();
        dependencies.Seed(TenantId, root.Id, other.Id);
        dependencies.Seed(TenantId, other.Id, child.Id);
        dependencies.Seed(TenantId, unrelated.Id, other.Id);
        var repository = new InMemoryTaskRepository([root, child, other, unrelated]);
        var service = new TaskHierarchyService(repository, dependencies);

        await service.DeleteWithDescendantsAsync(TenantId, root.Id, UserId, CancellationToken.None);

        Assert.Null(await dependencies.GetAsync(TenantId, root.Id, other.Id));
        Assert.Null(await dependencies.GetAsync(TenantId, other.Id, child.Id));
        Assert.NotNull(await dependencies.GetAsync(TenantId, unrelated.Id, other.Id));
    }

    private static (TaskHierarchyService Service, InMemoryTaskRepository Repository) Build(params TaskItem[] tasks)
    {
        var repository = new InMemoryTaskRepository(tasks);
        return (new TaskHierarchyService(repository, new InMemoryTaskDependencyRepository()), repository);
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
