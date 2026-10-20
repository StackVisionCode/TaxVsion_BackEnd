using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Tests;

public sealed class RecipientProjectionConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Registered_tenant_admin_is_projected_with_registration_time()
    {
        var userId = Guid.NewGuid();
        var occurredOn = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var owners = new FakeOwners();
        var portals = new FakePortals();
        var unitOfWork = new CountingUnitOfWork();

        await RecipientProjectionUserRegisteredConsumer.Handle(
            new UserRegisteredIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "owner@example.com",
                ActorType = "TenantAdmin",
                OccurredOn = occurredOn,
            },
            owners,
            portals,
            unitOfWork,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var projection = Assert.Single(owners.Items);
        Assert.Equal(userId, projection.UserId);
        Assert.Equal(occurredOn, projection.RegisteredAtUtc);
        Assert.True(projection.IsActive);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Registered_portal_user_is_projected_for_customer()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var owners = new FakeOwners();
        var portals = new FakePortals();

        await RecipientProjectionUserRegisteredConsumer.Handle(
            new UserRegisteredIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                CustomerId = customerId,
                Email = "portal@example.com",
                ActorType = "CustomerPortal",
            },
            owners,
            portals,
            new CountingUnitOfWork(),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var projection = Assert.Single(portals.Items);
        Assert.Equal(customerId, projection.CustomerId);
        Assert.Equal(userId, projection.UserId);
        Assert.True(projection.IsActive);
    }

    [Fact]
    public async Task Deactivation_and_reactivation_update_existing_owner()
    {
        var userId = Guid.NewGuid();
        var owner = TenantOwnerProjection.Create(TenantId, userId, DateTime.UtcNow);
        var owners = new FakeOwners(owner);
        var portals = new FakePortals();
        var unitOfWork = new CountingUnitOfWork();
        var correlation = new TestCorrelationContext();

        await RecipientProjectionUserDeactivatedConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "owner@example.com",
                ActorType = "TenantAdmin",
            },
            owners,
            portals,
            unitOfWork,
            correlation,
            CancellationToken.None
        );

        Assert.False(owner.IsActive);

        await RecipientProjectionUserReactivatedConsumer.Handle(
            new UserReactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "owner@example.com",
                ActorType = "TenantAdmin",
            },
            owners,
            portals,
            unitOfWork,
            correlation,
            CancellationToken.None
        );

        Assert.True(owner.IsActive);
        Assert.Equal(2, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Offboarding_marks_portal_account_inactive()
    {
        var userId = Guid.NewGuid();
        var portal = CustomerPortalAccountProjection.Create(TenantId, Guid.NewGuid(), userId);

        await RecipientProjectionUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = userId,
                Email = "portal@example.com",
                ActorType = "CustomerPortal",
                RemovedAtUtc = DateTime.UtcNow,
            },
            new FakeOwners(),
            new FakePortals(portal),
            new CountingUnitOfWork(),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.False(portal.IsActive);
    }

    private sealed class FakeOwners(params TenantOwnerProjection[] seed) : ITenantOwnerProjectionRepository
    {
        public List<TenantOwnerProjection> Items { get; } = [.. seed];

        public Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(Items.FirstOrDefault(x => x.TenantId == tenantId && x.IsActive)?.UserId);

        public Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                Items.Where(x => x.TenantId == tenantId && x.IsActive).Select(x => x.UserId).ToArray()
            );

        public Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(x => x.TenantId == tenantId && x.UserId == userId));

        public Task<TenantOwnerProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => GetAsync(tenantId, userId, ct);

        public Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default)
        {
            Items.Add(projection);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePortals(params CustomerPortalAccountProjection[] seed)
        : ICustomerPortalAccountProjectionRepository
    {
        public List<CustomerPortalAccountProjection> Items { get; } = [.. seed];

        public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(
                Items.FirstOrDefault(x => x.TenantId == tenantId && x.CustomerId == customerId && x.IsActive)?.UserId
            );

        public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(Items.FirstOrDefault(x => x.TenantId == tenantId && x.CustomerId == customerId));

        public Task<CustomerPortalAccountProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(Items.FirstOrDefault(x => x.TenantId == tenantId && x.UserId == userId));

        public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default)
        {
            Items.Add(projection);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class TestCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
