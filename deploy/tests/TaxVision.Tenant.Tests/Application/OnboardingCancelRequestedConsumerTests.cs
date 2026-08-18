using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Consumers;
using TaxVision.Tenant.Domain.Enums;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>PayFlow (Fase 17) — compensación de un onboarding cancelado que ya había creado el
/// tenant.</summary>
public sealed class OnboardingCancelRequestedConsumerTests
{
    [Fact]
    public async Task Closes_the_tenant_when_it_exists_for_the_onboarding()
    {
        var onboardingId = Guid.NewGuid();
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC", onboardingId).Value;
        var repo = new FakeTenantRepository { Existing = tenant };
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = onboardingId,
            Reason = "Provisioning failed permanently",
            OnboardingTenantId = tenant.Id,
            OnboardingUserId = null,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            repo,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantEntity>.Instance,
            CancellationToken.None
        );

        Assert.Equal(EnumTenantStatus.TenantStatus.Closed, tenant.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Is_a_noop_when_the_tenant_step_never_ran()
    {
        var repo = new FakeTenantRepository();
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            Reason = "Provisioning failed before Tenant step",
            OnboardingTenantId = null,
            OnboardingUserId = null,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            repo,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantEntity>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Is_idempotent_when_the_tenant_is_already_closed()
    {
        var onboardingId = Guid.NewGuid();
        var tenant = TenantEntity.Create("Acme Tax Office", "acme-tax", "UTC", onboardingId).Value;
        tenant.ChangeStatus(EnumTenantStatus.TenantStatus.Closed);
        var repo = new FakeTenantRepository { Existing = tenant };
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = onboardingId,
            Reason = "Replay of an already-processed cancel",
            OnboardingTenantId = tenant.Id,
            OnboardingUserId = null,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            repo,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantEntity>.Instance,
            CancellationToken.None
        );

        Assert.Equal(EnumTenantStatus.TenantStatus.Closed, tenant.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeTenantRepository : ITenantRepository
    {
        public TenantEntity? Existing { get; set; }

        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.OnboardingId == onboardingId ? Existing : null);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
