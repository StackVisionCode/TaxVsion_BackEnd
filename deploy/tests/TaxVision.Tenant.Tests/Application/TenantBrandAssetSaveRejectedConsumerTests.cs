using System.Linq;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Brands.Consumers;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

public sealed class TenantBrandAssetSaveRejectedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FileSaveRejectedIntegrationEvent Rejected(Guid fileId, string requestingService = "tenant") =>
        new()
        {
            TenantId = TenantId,
            FileId = fileId,
            RequestingService = requestingService,
            OwnerType = "Tenant",
            OwnerId = TenantId,
            FolderType = "Branding",
            Reason = "EmptyFile",
        };

    private static async Task<(RecordingMessageBus Bus, CountingUnitOfWork UnitOfWork)> HandleAsync(
        InMemoryBrandRepository repo,
        FileSaveRejectedIntegrationEvent msg
    )
    {
        var bus = new RecordingMessageBus();
        var unitOfWork = new CountingUnitOfWork();
        await TenantBrandAssetSaveRejectedConsumer.Handle(
            msg,
            repo,
            unitOfWork,
            bus,
            new NoopCorrelationContext(),
            NullLogger<TenantBrand>.Instance,
            TimeProvider.System,
            CancellationToken.None
        );
        return (bus, unitOfWork);
    }

    [Fact]
    public async Task A_rejected_crm_logo_is_discarded_and_its_removal_announced()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var (bus, unitOfWork) = await HandleAsync(repo, Rejected(fileId));

        Assert.Empty(repo.All.Single().Assets);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        // Scribe/Documents dejan de mostrar el logo — mismo evento que el delete.
        var evt = Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
        Assert.Equal(TenantId, evt.TenantId);
        Assert.Equal(TenantBrandSurfaces.Crm, evt.Surface);
    }

    [Fact]
    public async Task A_rejected_portal_logo_announces_the_removal_of_the_portal_surface()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Portal,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var (bus, _) = await HandleAsync(repo, Rejected(fileId));

        Assert.Empty(repo.All.Single().Assets);
        var evt = Assert.Single(bus.Published.OfType<TenantLogoRemovedIntegrationEvent>());
        Assert.Equal(TenantBrandSurfaces.Portal, evt.Surface);
    }

    [Fact]
    public async Task A_newer_logo_is_left_untouched()
    {
        var repo = new InMemoryBrandRepository();
        var rejectedFileId = Guid.NewGuid();
        var newerFileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b =>
            {
                b.SetAssetPending(BrandAssetKey.Logo, rejectedFileId, "image/png", 100, null, null, DateTime.UtcNow);
                // El tenant ya subió otro logo antes de que llegara el rechazo.
                b.SetAssetPending(BrandAssetKey.Logo, newerFileId, "image/png", 100, null, null, DateTime.UtcNow);
            }
        );

        var (bus, unitOfWork) = await HandleAsync(repo, Rejected(rejectedFileId));

        var asset = repo.All.Single().Assets.Single();
        Assert.Equal(newerFileId, asset.FileId);
        Assert.Equal(BrandAssetStatus.Pending, asset.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_rejection_requested_by_another_service_is_ignored()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var (bus, unitOfWork) = await HandleAsync(repo, Rejected(fileId, requestingService: "signature"));

        Assert.Single(repo.All.Single().Assets);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_unknown_file_id_is_a_no_op()
    {
        var repo = new InMemoryBrandRepository();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null, DateTime.UtcNow)
        );

        var (bus, unitOfWork) = await HandleAsync(repo, Rejected(Guid.NewGuid()));

        Assert.Single(repo.All.Single().Assets);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_replayed_rejection_does_not_announce_the_removal_twice()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        await HandleAsync(repo, Rejected(fileId));
        var (bus, unitOfWork) = await HandleAsync(repo, Rejected(fileId));

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_rejection_for_another_tenant_does_not_touch_this_tenant()
    {
        var repo = new InMemoryBrandRepository();
        var fileId = Guid.NewGuid();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow)
        );

        var (bus, _) = await HandleAsync(repo, Rejected(fileId) with { TenantId = Guid.NewGuid() });

        Assert.Single(repo.All.Single().Assets);
        Assert.Empty(bus.Published);
    }
}
