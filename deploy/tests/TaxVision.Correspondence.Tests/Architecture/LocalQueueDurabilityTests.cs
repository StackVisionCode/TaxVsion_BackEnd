extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Correspondence.Tests.Architecture;

// Todo mensaje con handler propio de Correspondence que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(CorrespondenceRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<CorrespondenceRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
