using BuildingBlocks.Domain;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Calendar.Domain.Appointments;
using Wolverine.Runtime;
using Xunit;

namespace TaxVision.Calendar.Tests.Architecture;

// El host arranca aunque un handler no compile: Wolverine genera su código con el primer mensaje.
public sealed class DomainEventHandlersCompileTests(CalendarRoutingHostFactory factory)
    : IClassFixture<CalendarRoutingHostFactory>
{
    [Fact]
    public void EveryDomainEventHandler_BuildsInTheRealHost()
    {
        var runtime = (WolverineRuntime)factory.Services.GetRequiredService<IWolverineRuntime>();
        var events = typeof(Appointment)
            .Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.NotNull(runtime.Handlers.HandlerFor(e)));
    }
}
