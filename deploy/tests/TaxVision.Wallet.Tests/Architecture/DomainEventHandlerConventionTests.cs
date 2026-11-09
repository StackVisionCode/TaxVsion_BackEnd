extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Wallet.Application;

namespace TaxVision.Wallet.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Wallet va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(TaxVision.Wallet.Domain.Wallet.Wallet).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(AssemblyMarker).Assembly];
}
