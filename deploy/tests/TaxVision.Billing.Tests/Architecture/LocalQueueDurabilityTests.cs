extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

// Todo mensaje con handler propio de Billing que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(BillingRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<BillingRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
