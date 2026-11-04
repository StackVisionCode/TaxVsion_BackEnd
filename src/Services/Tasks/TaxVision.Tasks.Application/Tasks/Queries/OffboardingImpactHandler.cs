using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

// Pre-flight de impacto del retiro: trabajo de este empleado en Tasks que se reasigna o cierra al retirarlo
// (tareas abiertas asignadas, pedidos al cliente abiertos cuyos avisos le llegan y timers en marcha).
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record OffboardingImpactResponse(int OpenTasks, int OpenClientRequests, int RunningTimers);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        ITaskRepository tasks,
        IClientRequestRepository clientRequests,
        CancellationToken ct
    )
    {
        var openTasks = await tasks.CountForAssigneeAsync(query.TenantId, query.UserId, status: null, ct);
        var openClientRequests = await clientRequests.CountOpenForStaffRecipientAsync(query.TenantId, query.UserId, ct);
        var runningTimers = await tasks.CountRunningTimersOfAsync(query.TenantId, query.UserId, ct);
        return new OffboardingImpactResponse(openTasks, openClientRequests, runningTimers);
    }
}
