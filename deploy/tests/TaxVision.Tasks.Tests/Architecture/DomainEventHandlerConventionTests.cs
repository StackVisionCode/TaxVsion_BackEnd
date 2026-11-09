extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Tasks.Application;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Tasks y de Client Forms va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies =>
        [typeof(TaskItem).Assembly, typeof(FormTemplate).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies =>
        [typeof(AssemblyMarker).Assembly, typeof(TaxVision.Tasks.ClientForms.Application.AssemblyMarker).Assembly];
}
