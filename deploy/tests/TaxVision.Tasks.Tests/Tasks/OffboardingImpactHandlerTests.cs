using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Tasks.Tests.Tasks;

// El pre-flight de impacto cuenta solo las tareas ABIERTAS asignadas al empleado (no las de otros).
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

    [Fact]
    public async Task Counts_open_tasks_assigned_to_the_employee()
    {
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var repo = new InMemoryTaskRepository(OpenTaskFor(userId), OpenTaskFor(userId), OpenTaskFor(other));

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, userId),
            repo,
            CancellationToken.None
        );

        Assert.Equal(2, result.OpenTasks);
    }

    [Fact]
    public async Task Returns_zero_when_the_employee_has_no_open_tasks()
    {
        var repo = new InMemoryTaskRepository();

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, Guid.NewGuid()),
            repo,
            CancellationToken.None
        );

        Assert.Equal(0, result.OpenTasks);
    }
}
