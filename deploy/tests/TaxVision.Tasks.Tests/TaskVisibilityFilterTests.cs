using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;

namespace TaxVision.Tasks.Tests;

/// <summary>
/// Visibilidad por asignación (P2): el handler del tablero solo restringe al actor cuando el flag está
/// encendido Y el actor NO ve todo (customers.view_all). En cualquier otro caso pasa null al lector
/// (= sin restricción), preservando el comportamiento previo hasta sembrar la proyección.
/// </summary>
public sealed class TaskVisibilityFilterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private static IOptions<TasksVisibilityOptions> Options(bool enabled) =>
        Microsoft.Extensions.Options.Options.Create(new TasksVisibilityOptions { Enabled = enabled });

    [Theory]
    [InlineData(true, false, true)] // flag ON + no ve todo  → restringe al actor
    [InlineData(true, true, false)] // flag ON + ve todo     → sin restricción
    [InlineData(false, false, false)] // flag OFF            → sin restricción
    [InlineData(false, true, false)] // flag OFF + ve todo   → sin restricción
    public async Task Board_restringe_al_actor_solo_con_flag_on_y_sin_view_all(
        bool enabled,
        bool canViewAll,
        bool shouldRestrict
    )
    {
        var reader = new CapturingBoardReader();
        var query = new GetTaskBoardColumnsQuery(
            Tenant,
            new TaskQueryFilter(RootOnly: true),
            null,
            20,
            TaskBoardSort.Due,
            Actor,
            canViewAll
        );

        await GetTaskBoardColumnsHandler.Handle(
            query,
            reader,
            new InMemoryWorkflowStatusRepository(),
            new FixedOfficeZone("America/New_York"),
            Options(enabled),
            CancellationToken.None
        );

        Assert.Equal(shouldRestrict ? Actor : (Guid?)null, reader.LastAssignee);
    }

    private sealed class CapturingBoardReader : ITaskBoardReader
    {
        public Guid? LastAssignee { get; private set; }

        public Task<IReadOnlyList<TaskBoardTally>> CountAsync(
            Guid tenantId,
            TaskQueryFilter filter,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            LastAssignee = assignedToUserId;
            return Task.FromResult<IReadOnlyList<TaskBoardTally>>([]);
        }

        public Task<IReadOnlyList<TaskCardResponse>> ListCardsAsync(
            Guid tenantId,
            TaskQueryFilter filter,
            TaskBoardSort sort,
            int skip,
            int take,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TaskCardResponse>>([]);
    }
}
