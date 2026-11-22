using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>R4: el tablero pide una fila de más para saber si cortó; el cuerpo sigue con el tope.</summary>
public sealed class GetTaskBoardHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    private static TaskItem Open(string title) =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

    private static Task<Application.Tasks.TaskBoardResponse> HandleAsync(InMemoryTaskRepository repository, int take) =>
        GetTaskBoardHandler.Handle(
            new GetTaskBoardQuery(TenantId, new TaskQueryFilter(OnlyOpen: true), take, Owner, CanViewAll: true),
            repository,
            Options.Create(new TasksVisibilityOptions { Enabled = false }),
            default
        );

    [Fact]
    public async Task More_open_tasks_than_the_cap_are_reported_as_truncated()
    {
        var repository = new InMemoryTaskRepository(Open("A"), Open("B"), Open("C"));

        var board = await HandleAsync(repository, take: 2);

        Assert.True(board.Truncated);
        Assert.Equal(2, board.TotalCount);
        Assert.Equal(2, board.Columns.Sum(column => column.Tasks.Count));
    }

    [Fact]
    public async Task At_or_under_the_cap_is_not_truncated()
    {
        var repository = new InMemoryTaskRepository(Open("A"), Open("B"));

        var board = await HandleAsync(repository, take: 2);

        Assert.False(board.Truncated);
        Assert.Equal(2, board.TotalCount);
    }
}
