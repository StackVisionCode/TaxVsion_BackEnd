using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class CreateSubtaskTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_subtask_sits_one_level_below_its_parent_and_counts()
    {
        var parent = NewRoot();

        var child = Subtask(parent).Value;

        Assert.Equal(parent.Id, child.ParentTaskId);
        Assert.Equal(1, child.Depth);
        Assert.Equal(1, parent.OpenSubtaskCount);
    }

    [Fact]
    public void The_subtask_inherits_the_customer_reference_of_its_parent()
    {
        var reference = TaskReference.Create(Guid.NewGuid(), 2025).Value;
        var parent = NewRoot(reference);

        var child = Subtask(parent).Value;

        Assert.Equal(reference, child.Reference);
    }

    [Fact]
    public void The_fourth_level_is_rejected()
    {
        var level0 = NewRoot();
        var level1 = Subtask(level0).Value;
        var level2 = Subtask(level1).Value;

        var result = Subtask(level2);

        Assert.Equal(TaskErrors.MaxDepthExceeded(TaskItem.MaxDepth), result.Error);
    }

    [Fact]
    public void Child_number_fifty_one_is_rejected()
    {
        var parent = NewRoot();
        for (var i = 0; i < TaskItem.MaxDirectChildren; i++)
            Assert.True(Subtask(parent).IsSuccess);

        var result = Subtask(parent);

        Assert.Equal(TaskErrors.TooManyChildren(TaskItem.MaxDirectChildren), result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_closed_parent_takes_no_new_subtasks(bool cancelled)
    {
        var parent = NewRoot();
        if (cancelled)
            parent.Cancel("El cliente desistió.", UserId, Now);
        else
            parent.Complete(UserId, Now);

        var result = Subtask(parent);

        Assert.Equal(TaskErrors.CannotAddSubtaskToClosedParent, result.Error);
    }

    [Fact]
    public void Closing_a_parent_with_open_subtasks_is_rejected()
    {
        var parent = NewRoot();
        Subtask(parent);

        var result = parent.Complete(UserId, Now);

        Assert.Equal(TaskErrors.HasOpenSubtasks(1), result.Error);
    }

    private static BuildingBlocks.Results.Result<TaskItem> Subtask(TaskItem parent) =>
        TaskItem.CreateSubtask(
            parent,
            UserId,
            TaskTitle.Create("Juntar los W-2").Value,
            null,
            TaskPriority.Normal,
            null,
            null,
            UserId,
            Now
        );

    private static TaskItem NewRoot(TaskReference? reference = null) =>
        TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                null,
                TaskPriority.Normal,
                reference ?? TaskReference.None,
                null,
                null,
                UserId,
                Now
            )
            .Value;
}
