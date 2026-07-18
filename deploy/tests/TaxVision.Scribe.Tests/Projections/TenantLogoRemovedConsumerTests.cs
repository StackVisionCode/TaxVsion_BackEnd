using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

public sealed class TenantLogoRemovedConsumerTests
{
    [Fact]
    public async Task Handle_soft_deletes_an_existing_TenantLogoRef()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        repository.Seed(TenantLogoRef.Create(tenantId, Guid.NewGuid(), "image/png", 1024, null, null, DateTime.UtcNow));
        var unitOfWork = new FakeUnitOfWork();
        var evt = new TenantLogoRemovedIntegrationEvent { TenantId = tenantId, RemovedAtUtc = DateTime.UtcNow };

        await TenantLogoRemovedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeCorrelationContext(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetByTenantIdAsync(tenantId);
        Assert.False(stored!.IsActive);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_skips_when_no_TenantLogoRef_exists()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new TenantLogoRemovedIntegrationEvent { TenantId = tenantId, RemovedAtUtc = DateTime.UtcNow };

        await TenantLogoRemovedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeCorrelationContext(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
