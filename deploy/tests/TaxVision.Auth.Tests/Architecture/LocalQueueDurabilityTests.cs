extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Auth.Tests.Architecture;

// Todo mensaje con handler propio de Auth que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(AuthRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<AuthRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
