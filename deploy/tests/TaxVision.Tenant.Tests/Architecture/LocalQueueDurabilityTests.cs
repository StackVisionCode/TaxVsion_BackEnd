extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;

namespace TaxVision.Tenant.Tests.Architecture;

// TEN-6: la invalidación de la caché de marca (y todo mensaje local de Tenant) sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(TenantRoutingHostFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<TenantRoutingHostFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
