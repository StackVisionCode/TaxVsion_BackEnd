extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Connectors.Tests.Architecture;

// Todo mensaje con handler propio de Connectors que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(ConnectorsRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<ConnectorsRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
