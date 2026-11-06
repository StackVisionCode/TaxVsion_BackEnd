using BuildingBlocks.Domain;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine.Runtime;

namespace TaxVision.Campaigns.Tests.Architecture;

// El host arranca aunque un handler no compile: Wolverine genera su código con el primer mensaje.
public sealed class DomainEventHandlersCompileTests(CampaignsRoutingHostFactory factory)
    : IClassFixture<CampaignsRoutingHostFactory>
{
    [Fact]
    public void EveryDomainEventHandler_BuildsInTheRealHost()
    {
        var runtime = (WolverineRuntime)factory.Services.GetRequiredService<IWolverineRuntime>();
        var events = typeof(CampaignRun)
            .Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.NotNull(runtime.Handlers.HandlerFor(e)));
    }
}
