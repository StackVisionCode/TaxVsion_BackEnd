extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Tests.Architecture;

// SIG-BB1: Signature no despacha eventos de dominio; un evento sin handler sería código muerto.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(SignatureRequest).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(SignatureRequestCompletedConsumer).Assembly];
}
