using System.Linq;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Al quitar el logo del CRM, Scribe se entera (TenantLogoRemoved); el resto de assets no.</summary>
public sealed class TenantBrandRemoveEventTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static async Task<RecordingMessageBus> RemoveAsync(BrandSurface surface, BrandAssetKey key)
    {
        var repo = new InMemoryBrandRepository();
        repo.Seed(TenantId, surface, b => b.SetAssetPending(key, Guid.NewGuid(), "image/png", 100, null, null));
        var bus = new RecordingMessageBus();

        await RemoveTenantBrandAssetHandler.Handle(
            new RemoveTenantBrandAssetCommand(TenantId, surface, key),
            repo,
            new FakeBrandingCloudStorageClient(),
            new CountingUnitOfWork(),
            new NoopCache(),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        return bus;
    }

    [Fact]
    public async Task Removing_the_crm_logo_publishes_TenantLogoRemoved()
    {
        var bus = await RemoveAsync(BrandSurface.Crm, BrandAssetKey.Logo);
        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task Removing_the_portal_logo_does_not_notify_scribe()
    {
        var bus = await RemoveAsync(BrandSurface.Portal, BrandAssetKey.Logo);
        Assert.Empty(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task Removing_the_crm_favicon_does_not_notify_scribe()
    {
        var bus = await RemoveAsync(BrandSurface.Crm, BrandAssetKey.Favicon);
        Assert.Empty(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task Resetting_the_crm_surface_with_a_logo_publishes_TenantLogoRemoved()
    {
        var repo = new InMemoryBrandRepository();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b =>
            {
                b.SetColor(BrandColorToken.Primary, "#AA0000");
                b.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null);
            }
        );
        var bus = new RecordingMessageBus();

        await ResetTenantBrandSurfaceHandler.Handle(
            new ResetTenantBrandSurfaceCommand(TenantId, BrandSurface.Crm),
            repo,
            new FakeBrandingCloudStorageClient(),
            new CountingUnitOfWork(),
            new NoopCache(),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
        Assert.Empty(repo.All.Single().Assets);
        Assert.Empty(repo.All.Single().Colors);
    }
}
