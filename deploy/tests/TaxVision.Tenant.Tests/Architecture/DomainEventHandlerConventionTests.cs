extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Tenant.Application.Tenants.Commands;

namespace TaxVision.Tenant.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Tenant va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(TaxVision.Tenant.Domain.Tenant).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(ChangeTenantStatusHandler).Assembly];
}
