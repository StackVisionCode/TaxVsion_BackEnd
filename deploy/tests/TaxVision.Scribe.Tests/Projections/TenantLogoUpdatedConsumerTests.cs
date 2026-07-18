using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

public sealed class TenantLogoUpdatedConsumerTests
{
    [Fact]
    public async Task Handle_creates_a_new_TenantLogoRef_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new TenantLogoUpdatedIntegrationEvent
        {
            TenantId = tenantId,
            CloudStorageFileId = Guid.NewGuid(),
            ContentType = "image/png",
            SizeBytes = 1024,
            Width = 180,
            Height = 60,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        await TenantLogoUpdatedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeCorrelationContext(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByTenantIdAsync(tenantId);
        Assert.NotNull(stored);
        Assert.Equal(evt.CloudStorageFileId, stored!.CloudStorageFileId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_updates_an_existing_TenantLogoRef_and_clears_soft_delete()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var existing = TenantLogoRef.Create(
            tenantId,
            Guid.NewGuid(),
            "image/jpeg",
            500,
            null,
            null,
            DateTime.UtcNow.AddDays(-5)
        );
        existing.MarkRemoved(DateTime.UtcNow.AddDays(-1));
        repository.Seed(existing);
        var unitOfWork = new FakeUnitOfWork();
        var newFileId = Guid.NewGuid();
        var evt = new TenantLogoUpdatedIntegrationEvent
        {
            TenantId = tenantId,
            CloudStorageFileId = newFileId,
            ContentType = "image/png",
            SizeBytes = 2048,
            Width = null,
            Height = null,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        await TenantLogoUpdatedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeCorrelationContext(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByTenantIdAsync(tenantId);
        Assert.Equal(newFileId, stored!.CloudStorageFileId);
        Assert.True(stored.IsActive);
    }
}
