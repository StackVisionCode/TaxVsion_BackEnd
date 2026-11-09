extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Campaigns.Application;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Campaigns va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(CampaignRun).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(AssemblyMarker).Assembly];
}
