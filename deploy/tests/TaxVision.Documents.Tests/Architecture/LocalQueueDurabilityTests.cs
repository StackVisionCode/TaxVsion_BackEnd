extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.Documents.Tests.Architecture;

// DOC-8: todo envío local de Documents sobrevive a un reinicio, sin depender de cada cola.
public sealed class LocalQueueDurabilityTests(DocumentsRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<DocumentsRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
