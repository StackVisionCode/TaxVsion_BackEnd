using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;

namespace TaxVision.Tasks.Tests;

/// <summary>
/// Visibilidad por asignación (P2): el handler del tablero solo restringe al actor cuando el flag está
/// encendido Y el actor NO ve todo (customers.view_all). En cualquier otro caso pasa null al repositorio
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
        var repo = new InMemoryTaskRepository();
        var query = new GetTaskBoardQuery(
            Tenant,
            new TaskQueryFilter(),
            Take: 200,
            ActorUserId: Actor,
            CanViewAll: canViewAll
        );

        await GetTaskBoardHandler.Handle(query, repo, Options(enabled), CancellationToken.None);

        Assert.Equal(shouldRestrict ? Actor : (Guid?)null, repo.LastBoardAssignee);
    }
}
