extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Architecture;

// CST-9: un evento de dominio sin handler quedaría en la cola sin avisar a nadie.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(FileObject).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(InitiateUploadHandler).Assembly];
}
