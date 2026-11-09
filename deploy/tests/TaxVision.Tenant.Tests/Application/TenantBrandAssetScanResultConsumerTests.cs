using System.Linq;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Application.Brands.Commands;
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
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, 40, 40, DateTime.UtcNow)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );

        var asset = repo.All.Single().Assets.Single();
        Assert.Equal(BrandAssetStatus.Confirmed, asset.Status);
        var evt = Assert.Single(bus.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
        Assert.Equal(fileId, evt.CloudStorageFileId);
        Assert.Equal(TenantBrandSurfaces.Crm, evt.Surface);
        Assert.Equal(asset.ConfirmedAtUtc, evt.UpdatedAtUtc);
    }

    [Fact]
    public async Task Confirming_a_portal_logo_publishes_it_with_the_portal_surface()
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(BrandAssetStatus.Confirmed, repo.All.Single().Assets.Single().Status);
        // El logo del portal sale con su superficie: es el que ven los clientes.
        var evt = Assert.Single(bus.Published.OfType<TenantLogoUpdatedIntegrationEvent>());
        Assert.Equal(fileId, evt.CloudStorageFileId);
        Assert.Equal(TenantBrandSurfaces.Portal, evt.Surface);
    }

    // El handler ya no recibe la caché: no puede tocarla antes del commit.
    [Fact]
    public async Task FileAvailable_DoesNotInvalidateCacheInline_PublishesInvalidationCommand()
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.Equal(
            new InvalidateTenantBrandCacheCommand(TenantId, BrandSurface.Portal),
            Assert.Single(bus.Published.OfType<InvalidateTenantBrandCacheCommand>())
        );
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
            b => b.SetAssetPending(BrandAssetKey.Favicon, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            Available(fileId),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
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
            b => b.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null, DateTime.UtcNow)
        );

        // Evento de un fileId que no es de ninguna marca de este tenant (p.ej. logo del modelo viejo).
        await TenantBrandAssetScanResultConsumer.Handle(
            Available(Guid.NewGuid()),
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
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
                bus,
                new NoopCorrelationContext(),
                NullLogger<TenantBrand>.Instance,
                TimeProvider.System,
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
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
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
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
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
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var bus = await DiscardAsync(repo, fileId, blockedByPolicy: true);

        Assert.Empty(repo.All.Single().Assets);
        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task An_infected_portal_logo_announces_the_removal_of_the_portal_surface_only()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var bus = await DiscardAsync(repo, fileId);

        Assert.Empty(repo.All.Single().Assets);
        var evt = Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
        Assert.Equal(TenantBrandSurfaces.Portal, evt.Surface);
    }

    [Fact]
    public async Task An_infected_portal_favicon_is_discarded_without_announcing_anything()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Favicon, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var bus = await DiscardAsync(repo, fileId);

        Assert.Empty(repo.All.Single().Assets);
        Assert.Empty(bus.Published.OfType<IIntegrationEvent>());
    }

    [Fact]
    public async Task A_replayed_infected_event_does_not_announce_the_removal_twice()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        await DiscardAsync(repo, fileId);
        var bus = await DiscardAsync(repo, fileId);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task FileInfected_ForConfirmedAsset_RemovesIt()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b =>
            {
                b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);
                b.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);
            }
        );

        var bus = await DiscardAsync(repo, fileId);

        // Red de seguridad: un archivo confirmado que resultó infectado deja de servirse.
        Assert.Empty(repo.All.Single().Assets);
        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }

    [Fact]
    public async Task FileInfected_ForReplacedAsset_KeepsTheNewFile()
    {
        var repo = new InMemoryBrandRepository();
        var newFileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, newFileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var bus = await DiscardAsync(repo, Guid.NewGuid());

        Assert.Equal(newFileId, repo.All.Single().Assets.Single().FileId);
        Assert.Empty(bus.Published);
    }

    [Theory]
    [InlineData("blob_missing")]
    [InlineData("scan_stalled")]
    public async Task FileScanFailed_ForPendingAsset_DiscardsIt(string reason)
    {
        var repo = new InMemoryBrandRepository();
        var bus = new RecordingMessageBus();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        await TenantBrandAssetScanResultConsumer.Handle(
            new FileScanFailedIntegrationEvent
            {
                TenantId = TenantId,
                FileId = fileId,
                ObjectKey = $"tenant-branding/{fileId:N}/logo.png",
                Reason = reason,
            },
            repo,
            new CountingUnitOfWork(),
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );

        // El CRM deja de ver "Pending" y puede volver a subir.
        Assert.Empty(repo.All.Single().Assets);
        Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
    }
}
