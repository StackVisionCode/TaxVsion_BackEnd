extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Customer.Tests.Architecture;

// Todo mensaje con handler propio de Customer que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(CustomerRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<CustomerRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
