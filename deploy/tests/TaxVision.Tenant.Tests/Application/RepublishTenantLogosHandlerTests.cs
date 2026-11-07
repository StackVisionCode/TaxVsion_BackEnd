using System.Linq;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>La republicación anuncia solo logos confirmados de CRM y Portal, con su fecha original.</summary>
public sealed class RepublishTenantLogosHandlerTests
{
    private static readonly DateTime ConfirmedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static void SeedConfirmed(
        InMemoryBrandRepository repo,
        Guid tenantId,
        BrandSurface surface,
        BrandAssetKey key,
        Guid fileId
    ) =>
        repo.Seed(
            tenantId,
            surface,
            b =>
            {
                b.SetAssetPending(key, fileId, "image/png", 100, 40, 20, DateTime.UtcNow);
                b.ConfirmAsset(key, fileId, "image/png", 100, 40, 20, ConfirmedAt);
            }
        );

    private static async Task<RecordingMessageBus> RunAsync(InMemoryBrandRepository repo)
    {
        var bus = new RecordingMessageBus();
        var result = await RepublishTenantLogosHandler.Handle(
            new RepublishTenantLogosCommand(),
            repo,
            bus,
            new NoopCorrelationContext(),
            NullLogger<RepublishTenantLogosCommand>.Instance,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        Assert.Equal(bus.Published.Count, result.Value.Published);
        return bus;
    }

    [Fact]
    public async Task Publishes_the_confirmed_crm_and_portal_logos_with_their_surface_and_original_date()
    {
        var repo = new InMemoryBrandRepository();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var crmFile = Guid.NewGuid();
        var portalFile = Guid.NewGuid();
        SeedConfirmed(repo, tenantA, BrandSurface.Crm, BrandAssetKey.Logo, crmFile);
        SeedConfirmed(repo, tenantB, BrandSurface.Portal, BrandAssetKey.Logo, portalFile);

        var bus = await RunAsync(repo);

        var events = bus.Published.OfType<TenantLogoUpdatedIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);
        var crm = Assert.Single(events, e => e.TenantId == tenantA);
        Assert.Equal((crmFile, TenantBrandSurfaces.Crm), (crm.CloudStorageFileId, crm.Surface));
        var portal = Assert.Single(events, e => e.TenantId == tenantB);
        Assert.Equal((portalFile, TenantBrandSurfaces.Portal), (portal.CloudStorageFileId, portal.Surface));
        Assert.All(events, e => Assert.Equal(ConfirmedAt, e.UpdatedAtUtc));
    }

    [Fact]
    public async Task Skips_favicons_pending_logos_and_surfaces_that_do_not_leave_the_service()
    {
        var repo = new InMemoryBrandRepository();
        var tenantId = Guid.NewGuid();
        SeedConfirmed(repo, tenantId, BrandSurface.Crm, BrandAssetKey.Favicon, Guid.NewGuid());
        SeedConfirmed(repo, tenantId, BrandSurface.Mobile, BrandAssetKey.Logo, Guid.NewGuid());
        repo.Seed(
            tenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null, DateTime.UtcNow)
        );

        var bus = await RunAsync(repo);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Running_it_twice_announces_the_same_logos()
    {
        var repo = new InMemoryBrandRepository();
        SeedConfirmed(repo, Guid.NewGuid(), BrandSurface.Crm, BrandAssetKey.Logo, Guid.NewGuid());

        var first = await RunAsync(repo);
        var second = await RunAsync(repo);

        var a = Assert.Single(first.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
        var b = Assert.Single(second.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
        Assert.Equal(
            (a.TenantId, a.Surface, a.CloudStorageFileId, a.UpdatedAtUtc),
            (b.TenantId, b.Surface, b.CloudStorageFileId, b.UpdatedAtUtc)
        );
    }
}
