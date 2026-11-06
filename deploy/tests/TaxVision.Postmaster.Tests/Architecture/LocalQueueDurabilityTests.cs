extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Postmaster.Tests.Architecture;

// Todo mensaje con handler propio de Postmaster que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(PostmasterRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<PostmasterRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
