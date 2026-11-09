extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Tenant.Tests.Integration;

namespace TaxVision.Tenant.Tests.Architecture;

// TEN-6: la invalidación de la caché de marca (y todo mensaje local de Tenant) sobrevive a un reinicio.
public sealed class LocalQueueDurabilityTests(TenantApiFactory factory)
    : LocalQueueDurabilityRules,
        IClassFixture<TenantApiFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
