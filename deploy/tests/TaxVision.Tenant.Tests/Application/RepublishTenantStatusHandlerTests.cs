using BuildingBlocks.Messaging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Tests.TestSupport;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

public sealed class RepublishTenantStatusHandlerTests
{
    [Fact]
    public async Task Handle_ClosedTenant_PublishesCurrentStatus()
    {
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC", DateTime.UtcNow, Guid.NewGuid()).Value;
        tenant.CloseForOnboardingCancellation();
        var bus = new RecordingMessageBus();

        var result = await RepublishTenantStatusHandler.Handle(
            new RepublishTenantStatusCommand(tenant.Id),
            new SingleTenantRepository(tenant),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var published = Assert.Single(bus.Published.OfType<TenantStatusChangedIntegrationEvent>());
        Assert.Equal(tenant.Id, published.ChangedTenantId);
        Assert.Equal(nameof(EnumTenantStatus.TenantStatus.Closed), published.Status);
        Assert.False(published.IsActive);
    }

    [Fact]
    public async Task Handle_PublishesTheTenantVersion()
    {
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC", DateTime.UtcNow).Value;
        tenant.Suspend();
        var bus = new RecordingMessageBus();

        await RepublishTenantStatusHandler.Handle(
            new RepublishTenantStatusCommand(tenant.Id),
            new SingleTenantRepository(tenant),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(1, Assert.Single(bus.Published.OfType<TenantStatusChangedIntegrationEvent>()).TenantVersion);
    }

    [Fact]
    public async Task HandleAll_EnqueuesOneCommandPerTenantAcrossPages()
    {
        var ids = Enumerable.Range(0, 450).Select(_ => Guid.NewGuid()).ToList();
        var bus = new RecordingMessageBus();

        var enqueued = await RepublishAllTenantStatusHandler.Handle(
            new RepublishAllTenantStatusCommand(),
            new PagedTenants(ids),
            bus,
            CancellationToken.None
        );

        Assert.Equal(450, enqueued);
        Assert.Equal(ids, bus.Published.OfType<RepublishTenantStatusCommand>().Select(c => c.TenantId));
    }

    private sealed class PagedTenants(IReadOnlyList<Guid> ids) : ITenantReadService
    {
        public Task<IReadOnlyList<TenantResponse>> GetPageAsync(int page, int size, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantResponse>>(
                ids.Skip((page - 1) * size).Take(size).Select(id => new TenantResponse(id, "n", "s", "UTC")).ToList()
            );
    }

    [Fact]
    public async Task Handle_UnknownTenant_ReturnsNotFoundAndPublishesNothing()
    {
        var bus = new RecordingMessageBus();

        var result = await RepublishTenantStatusHandler.Handle(
            new RepublishTenantStatusCommand(Guid.NewGuid()),
            new SingleTenantRepository(null),
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal("Tenant.NotFound", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    private sealed class SingleTenantRepository(TenantEntity? tenant) : ITenantRepository
    {
        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(tenant is not null && tenant.Id == id ? tenant : null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
