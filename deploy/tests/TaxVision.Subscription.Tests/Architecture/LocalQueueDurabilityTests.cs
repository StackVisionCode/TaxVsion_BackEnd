extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.Subscription.Tests.Architecture;

// Todo mensaje con handler propio de Subscription que se publica a una cola local (RecalculateEntitlements y
// sus abanicos) sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(SubscriptionRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<SubscriptionRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
