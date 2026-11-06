extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Auth va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(User).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(DeactivateUserHandler).Assembly];
}
