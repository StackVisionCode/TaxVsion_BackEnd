extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Connectors.Application.Accounts.DomainEvents;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Connectors va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(TenantEmailAccount).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies =>
        [typeof(TenantEmailAccountErroredDomainEventHandler).Assembly];
}
