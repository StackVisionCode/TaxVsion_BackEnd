using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.ClientRequests;
using Xunit;

namespace TaxVision.Tasks.Tests.Tasks;

// El pre-flight de impacto cuenta lo del empleado (no lo de otros): tareas abiertas asignadas, pedidos al
// cliente abiertos cuyos avisos le llegan y timers en marcha.
public sealed class OffboardingImpactHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static TaskItem OpenTaskFor(Guid assignee) =>
        TaskItem
            .Create(
                Tenant,
                Guid.NewGuid(),
                TaskTitle.Create("Open task").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

    private static ClientRequest RequestBy(Guid requester) =>
        ClientRequest.Create(Tenant, Guid.NewGuid(), requester, null, "W-2", null, null, DateTime.UtcNow).Value;

    [Fact]
    public async Task Counts_open_tasks_assigned_to_the_employee()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var repo = new InMemoryTaskRepository(OpenTaskFor(userId), OpenTaskFor(userId), OpenTaskFor(other));

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, userId),
            repo,
            new InMemoryClientRequestRepository(),
            CancellationToken.None
        );

        Assert.Equal(2, result.OpenTasks);
    }

    [Fact]
    public async Task Counts_open_client_requests_and_running_timers_of_the_employee()
    {
        var userId = Guid.NewGuid();
        var task = OpenTaskFor(Guid.NewGuid());
        task.StartTimer(userId, isBillable: true, DateTime.UtcNow);
        task.StartTimer(Guid.NewGuid(), isBillable: true, DateTime.UtcNow);
        var closed = RequestBy(userId);
        closed.Cancel(userId, null, DateTime.UtcNow);
        var requests = new InMemoryClientRequestRepository(RequestBy(userId), closed, RequestBy(Guid.NewGuid()));

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, userId),
            new InMemoryTaskRepository(task),
            requests,
            CancellationToken.None
        );

        Assert.Equal(1, result.OpenClientRequests);
        Assert.Equal(1, result.RunningTimers);
        Assert.Equal(0, result.OpenTasks);
    }

    [Fact]
    public async Task Returns_zero_when_the_employee_has_nothing_pending()
    {
        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, Guid.NewGuid()),
            new InMemoryTaskRepository(),
            new InMemoryClientRequestRepository(),
            CancellationToken.None
        );

        Assert.Equal(new OffboardingImpactResponse(0, 0, 0), result);
    }
}
