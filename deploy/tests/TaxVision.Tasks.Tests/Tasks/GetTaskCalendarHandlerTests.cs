using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>B3 (Calendar): el calendario de tareas valida el rango, recibe los filtros del tablero y avisa si cortó.</summary>
public sealed class GetTaskCalendarHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime From = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IOptions<TasksVisibilityOptions> NoVisibility() =>
        Options.Create(new TasksVisibilityOptions { Enabled = false });

    private static TaskItem Due(DateTime dueAtUtc) =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("File the return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                DueDate.Create(dueAtUtc, "America/New_York", isStatutory: false).Value,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

    private static GetTaskCalendarQuery Query(
        DateTime from,
        DateTime to,
        TaskQueryFilter? filter = null,
        int take = 500
    ) => new(TenantId, from, to, filter ?? new TaskQueryFilter(), take, Owner, CanViewAll: true);

    [Fact]
    public async Task Inverted_range_is_rejected()
    {
        var result = await GetTaskCalendarHandler.Handle(
            Query(From, From.AddDays(-1)),
            new InMemoryTaskRepository(),
            NoVisibility(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tasks.Calendar.RangeInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Range_wider_than_62_days_is_rejected()
    {
        var result = await GetTaskCalendarHandler.Handle(
            Query(From, From.AddDays(63)),
            new InMemoryTaskRepository(),
            NoVisibility(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tasks.Calendar.RangeInvalid", result.Error.Code);
    }

    [Fact]
    public async Task More_tasks_than_the_cap_are_reported_as_truncated()
    {
        var repository = new InMemoryTaskRepository(Due(From.AddDays(1)), Due(From.AddDays(2)), Due(From.AddDays(3)));

        var result = await GetTaskCalendarHandler.Handle(
            Query(From, From.AddDays(30), take: 2),
            repository,
            NoVisibility(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task The_board_filters_reach_the_repository()
    {
        var repository = new InMemoryTaskRepository();
        var customer = Guid.NewGuid();
        var filter = new TaskQueryFilter(CustomerId: customer, TaxYear: 2025, OnlyOpen: true);

        var result = await GetTaskCalendarHandler.Handle(
            Query(From, From.AddDays(30), filter),
            repository,
            NoVisibility(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Truncated);
        Assert.Equal(filter, repository.LastCalendarFilter);
    }
}
