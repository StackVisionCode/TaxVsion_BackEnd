extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Customer.Application.Customers.Commands.Create;

namespace TaxVision.Customer.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Customer va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(TaxVision.Customer.Domain.Customers.Customer).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(CreateCustomerHandler).Assembly];
}
