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
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC", Guid.NewGuid()).Value;
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
