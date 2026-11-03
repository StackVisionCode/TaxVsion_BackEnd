using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Projections;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Projections;

public sealed class TenantLogoRemovedConsumerTests
{
    private static Task HandleAsync(
        TenantLogoRemovedIntegrationEvent evt,
        FakeTenantLogoRefRepository repository,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantLogoRemovedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeCorrelationContext(),
            NullLogger<TenantLogoRef>.Instance,
            CancellationToken.None
        );

    private static TenantLogoRef Logo(Guid tenantId, TenantLogoSurface surface, DateTime updatedAtUtc) =>
        TenantLogoRef.Create(tenantId, surface, Guid.NewGuid(), "image/png", 1024, null, null, updatedAtUtc);

    [Fact]
    public async Task Handle_soft_deletes_an_existing_TenantLogoRef()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        repository.Seed(Logo(tenantId, TenantLogoSurface.Crm, DateTime.UtcNow.AddMinutes(-1)));
        var unitOfWork = new FakeUnitOfWork();
        var evt = new TenantLogoRemovedIntegrationEvent { TenantId = tenantId, RemovedAtUtc = DateTime.UtcNow };

        await HandleAsync(evt, repository, unitOfWork);

        var stored = await repository.GetAsync(tenantId, TenantLogoSurface.Crm);
        Assert.False(stored!.IsActive);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_removing_the_portal_logo_keeps_the_crm_logo_active()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var past = DateTime.UtcNow.AddMinutes(-1);
        repository.Seed(Logo(tenantId, TenantLogoSurface.Crm, past));
        repository.Seed(Logo(tenantId, TenantLogoSurface.Portal, past));
        var evt = new TenantLogoRemovedIntegrationEvent
        {
            TenantId = tenantId,
            Surface = TenantBrandSurfaces.Portal,
            RemovedAtUtc = DateTime.UtcNow,
        };

        await HandleAsync(evt, repository, new FakeUnitOfWork());

        Assert.False((await repository.GetAsync(tenantId, TenantLogoSurface.Portal))!.IsActive);
        Assert.True((await repository.GetAsync(tenantId, TenantLogoSurface.Crm))!.IsActive);
    }

    [Fact]
    public async Task Handle_ignores_a_removal_older_than_the_projected_logo()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var now = DateTime.UtcNow;
        repository.Seed(Logo(tenantId, TenantLogoSurface.Crm, now));
        var unitOfWork = new FakeUnitOfWork();
        var evt = new TenantLogoRemovedIntegrationEvent { TenantId = tenantId, RemovedAtUtc = now.AddMinutes(-5) };

        await HandleAsync(evt, repository, unitOfWork);

        Assert.True((await repository.GetAsync(tenantId, TenantLogoSurface.Crm))!.IsActive);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_skips_when_no_TenantLogoRef_exists()
    {
        var tenantId = Guid.NewGuid();
        var repository = new FakeTenantLogoRefRepository();
        var unitOfWork = new FakeUnitOfWork();
        var evt = new TenantLogoRemovedIntegrationEvent { TenantId = tenantId, RemovedAtUtc = DateTime.UtcNow };

        await HandleAsync(evt, repository, unitOfWork);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
