using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

// Pre-flight de impacto (punto 3.2): cuánto trabajo de este empleado en Tasks habría que reasignar antes
// de retirarlo. Por ahora = tareas abiertas que tiene asignadas.
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record OffboardingImpactResponse(int OpenTasks);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var openTasks = await tasks.CountForAssigneeAsync(query.TenantId, query.UserId, status: null, ct);
        return new OffboardingImpactResponse(openTasks);
    }
}
