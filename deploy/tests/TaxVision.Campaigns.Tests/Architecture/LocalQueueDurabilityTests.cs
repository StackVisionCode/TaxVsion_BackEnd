extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Campaigns.Tests.Architecture;

// Todo mensaje con handler propio de Campaigns que se publica a una cola local sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(CampaignsRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<CampaignsRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
