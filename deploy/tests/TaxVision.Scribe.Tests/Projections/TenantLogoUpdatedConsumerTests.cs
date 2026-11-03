using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

public sealed class TenantLogoUpdatedConsumerTests
{
    private static TenantLogoUpdatedIntegrationEvent Updated(
        Guid tenantId,
        Guid fileId,
        DateTime updatedAtUtc,
        string surface = TenantBrandSurfaces.Crm
    ) =>
        new()
        {
            TenantId = tenantId,
            Surface = surface,
            CloudStorageFileId = fileId,
            ContentType = "image/png",
            SizeBytes = 1024,
            Width = 180,
            Height = 60,
            UpdatedAtUtc = updatedAtUtc,
        };

    private static Task HandleAsync(
        TenantLogoUpdatedIntegrationEvent evt,
        FakeTenantLogoRefRepository repository,
        FakeUnitOfWork unitOfWork,
        IMemoryCache? cache = null
    ) =>
        TenantLogoUpdatedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            cache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeCorrelationContext(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_creates_a_new_TenantLogoRef_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = Updated(tenantId, Guid.NewGuid(), DateTime.UtcNow);

        await HandleAsync(evt, repository, unitOfWork);

        var stored = await repository.GetAsync(tenantId, TenantLogoSurface.Crm);
        Assert.NotNull(stored);
        Assert.Equal(evt.CloudStorageFileId, stored!.CloudStorageFileId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_without_surface_in_an_old_message_is_stored_as_crm()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        // Mensaje viejo: el contrato deserializa Surface ausente como Crm.
        var evt = new TenantLogoUpdatedIntegrationEvent
        {
            TenantId = tenantId,
            CloudStorageFileId = Guid.NewGuid(),
            ContentType = "image/png",
            SizeBytes = 10,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        await HandleAsync(evt, repository, new FakeUnitOfWork());

        Assert.NotNull(await repository.GetAsync(tenantId, TenantLogoSurface.Crm));
    }

    [Fact]
    public async Task Handle_stores_the_portal_logo_without_touching_the_crm_one()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var crmFile = Guid.NewGuid();
        var portalFile = Guid.NewGuid();

        await HandleAsync(Updated(tenantId, crmFile, DateTime.UtcNow), repository, new FakeUnitOfWork());
        await HandleAsync(
            Updated(tenantId, portalFile, DateTime.UtcNow, TenantBrandSurfaces.Portal),
            repository,
            new FakeUnitOfWork()
        );

        Assert.Equal(crmFile, (await repository.GetAsync(tenantId, TenantLogoSurface.Crm))!.CloudStorageFileId);
        Assert.Equal(portalFile, (await repository.GetAsync(tenantId, TenantLogoSurface.Portal))!.CloudStorageFileId);
    }

    [Fact]
    public async Task Handle_updates_an_existing_TenantLogoRef_and_clears_soft_delete()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var existing = TenantLogoRef.Create(
            tenantId,
            TenantLogoSurface.Crm,
            Guid.NewGuid(),
            "image/jpeg",
            500,
            null,
            null,
            DateTime.UtcNow.AddDays(-5)
        );
        existing.MarkRemoved(DateTime.UtcNow.AddDays(-1));
        repository.Seed(existing);
        var newFileId = Guid.NewGuid();

        await HandleAsync(Updated(tenantId, newFileId, DateTime.UtcNow), repository, new FakeUnitOfWork());

        var stored = await repository.GetAsync(tenantId, TenantLogoSurface.Crm);
        Assert.Equal(newFileId, stored!.CloudStorageFileId);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Handle_ignores_an_event_older_than_the_projected_logo()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var current = Guid.NewGuid();
        var now = DateTime.UtcNow;
        repository.Seed(
            TenantLogoRef.Create(tenantId, TenantLogoSurface.Crm, current, "image/png", 1, null, null, now)
        );
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Updated(tenantId, Guid.NewGuid(), now.AddMinutes(-5)), repository, unitOfWork);

        Assert.Equal(current, (await repository.GetAsync(tenantId, TenantLogoSurface.Crm))!.CloudStorageFileId);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_replayed_event_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var evt = Updated(tenantId, Guid.NewGuid(), DateTime.UtcNow);

        await HandleAsync(evt, repository, new FakeUnitOfWork());
        await HandleAsync(evt, repository, new FakeUnitOfWork());

        var stored = Assert.Single(await repository.ListByTenantAsync(tenantId));
        Assert.Equal(evt.CloudStorageFileId, stored.CloudStorageFileId);
    }

    [Fact]
    public async Task Handle_ignores_an_unknown_surface()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var unitOfWork = new FakeUnitOfWork();

        await HandleAsync(Updated(tenantId, Guid.NewGuid(), DateTime.UtcNow, "Kiosk"), repository, unitOfWork);

        Assert.Empty(await repository.ListByTenantAsync(tenantId));
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_invalidates_the_cached_logo_of_every_audience()
    {
        var tenantId = Guid.NewGuid();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var stale = new LogoAsset(Guid.NewGuid(), "image/png", 1, false);
        foreach (var audience in Enum.GetValues<RenderAudience>())
            cache.Set(LogoCacheKeys.Tenant(tenantId, audience), stale, new MemoryCacheEntryOptions { Size = 1 });

        await HandleAsync(
            Updated(tenantId, Guid.NewGuid(), DateTime.UtcNow),
            new FakeTenantLogoRefRepository(),
            new FakeUnitOfWork(),
            cache
        );

        Assert.All(
            Enum.GetValues<RenderAudience>(),
            audience => Assert.False(cache.TryGetValue(LogoCacheKeys.Tenant(tenantId, audience), out _))
        );
    }
}
