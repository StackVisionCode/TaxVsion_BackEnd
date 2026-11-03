using System.Linq;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Brands.Consumers;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

public sealed class TenantBrandAssetScanResultConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FileAvailableIntegrationEvent Available(Guid fileId) =>
        new()
        {
            TenantId = TenantId,
            FileId = fileId,
            ObjectKey = $"tenant-branding/{fileId:N}/logo.png",
            ContentType = "image/png",
            SizeBytes = 100,
            ChecksumSha256 = "abc123",
            CreatedBy = Guid.NewGuid(),
        };

    [Fact]
    public async Task Confirming_a_crm_logo_confirms_the_asset_and_notifies_scribe()
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, 40, 40)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            CancellationToken.None
        );

        var asset = repo.All.Single().Assets.Single();
        Assert.Equal(BrandAssetStatus.Confirmed, asset.Status);
        // Email (Scribe) se entera del logo del CRM — mismo evento que el modelo viejo.
        var evt = Assert.Single(bus.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
        Assert.Equal(fileId, evt.CloudStorageFileId);
    }

    [Fact]
    public async Task Confirming_a_portal_logo_confirms_but_does_not_notify_scribe()
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            CancellationToken.None
        );

        Assert.Equal(BrandAssetStatus.Confirmed, repo.All.Single().Assets.Single().Status);
        // El logo del portal NO alimenta el email.
        Assert.Empty(bus.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task Confirming_a_favicon_does_not_notify_scribe()
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Favicon, fileId, "image/png", 100, null, null)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task An_unrelated_file_id_is_ignored()
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null)
        );

        // Evento de un fileId que no es de ninguna marca de este tenant (p.ej. logo del modelo viejo).
        await TenantBrandAssetScanResultConsumer.Handle(
            Available(Guid.NewGuid()),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            CancellationToken.None
        );

        // No confirmó nada ni publicó nada.
        Assert.Equal(BrandAssetStatus.Pending, repo.All.Single().Assets.Single().Status);
        Assert.Empty(bus.Published);
    }

    private static async Task<RecordingMessageBus> DiscardAsync(
        InMemoryBrandRepository repo,
        Guid fileId,
        bool blockedByPolicy = false
    )
    {
        var bus = new RecordingMessageBus();
        if (blockedByPolicy)
        {
            await TenantBrandAssetScanResultConsumer.Handle(
                new FileBlockedByPolicyIntegrationEvent
                {
                    TenantId = TenantId,
                    FileId = fileId,
                    ObjectKey = $"tenant-branding/{fileId:N}/logo.png",
                    PolicyReason = "nsfw",
                    CreatedBy = Guid.NewGuid(),
                },
                repo,
                new CountingUnitOfWork(),
                new NoopCache(),
                bus,
                new NoopCorrelationContext(),
                NullLogger<TenantBrand>.Instance,
                CancellationToken.None
            );
            return bus;
        }

        await TenantBrandAssetScanResultConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = $"tenant-branding/{fileId:N}/logo.png",
                ScanReport = "EICAR test",
            },
            repo,
            new CountingUnitOfWork(),
            new NoopCache(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            CancellationToken.None
        );
        return bus;
    }

    [Fact]
    public async Task An_infected_crm_logo_is_discarded_and_its_removal_announced()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null)
        );

        var bus = await DiscardAsync(repo, fileId);

        Assert.Empty(repo.All.Single().Assets);
        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task A_policy_blocked_crm_logo_is_discarded_and_its_removal_announced()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null)
        );

        var bus = await DiscardAsync(repo, fileId, blockedByPolicy: true);

        Assert.Empty(repo.All.Single().Assets);
        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task An_infected_portal_logo_is_discarded_without_notifying_scribe()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null)
        );

        var bus = await DiscardAsync(repo, fileId);

        Assert.Empty(repo.All.Single().Assets);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_replayed_infected_event_does_not_announce_the_removal_twice()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null)
        );

        await DiscardAsync(repo, fileId);
        var bus = await DiscardAsync(repo, fileId);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_infected_event_for_a_confirmed_logo_is_a_no_op()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b =>
            {
                b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null);
                b.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);
            }
        );

        var bus = await DiscardAsync(repo, fileId);

        Assert.Equal(BrandAssetStatus.Confirmed, repo.All.Single().Assets.Single().Status);
        Assert.Empty(bus.Published);
    }
}
