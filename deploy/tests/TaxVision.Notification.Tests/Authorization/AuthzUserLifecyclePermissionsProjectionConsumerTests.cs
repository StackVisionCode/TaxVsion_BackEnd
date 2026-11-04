using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Authorization.Abstractions;
using TaxVision.Notification.Application.Authorization.Consumers;
using TaxVision.Notification.Domain.Authorization;

namespace TaxVision.Notification.Tests.Authorization;

/// <summary>Suspender o retirar apaga la proyección que autoriza las requests; reactivar la vuelve a encender.</summary>
public sealed class AuthzUserLifecyclePermissionsProjectionConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Offboarding_marks_the_projection_inactive()
    {
        var projection = Projection();
        var unitOfWork = new CountingUnitOfWork();

        await AuthzUserLifecyclePermissionsProjectionConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = projection.UserId,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                OffboardedByUserId = Guid.NewGuid(),
                RemovedAtUtc = DateTime.UtcNow,
            },
            new FakeRepository(projection),
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.False(projection.IsActive);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Deactivation_marks_the_projection_inactive_and_a_repeat_is_a_noop()
    {
        var projection = Projection();
        var repository = new FakeRepository(projection);
        var unitOfWork = new CountingUnitOfWork();
        var evt = Deactivated(projection.UserId);

        await AuthzUserLifecyclePermissionsProjectionConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );
        await AuthzUserLifecyclePermissionsProjectionConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.False(projection.IsActive);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reactivation_marks_the_projection_active_again()
    {
        var projection = Projection();
        projection.MarkInactive();
        var unitOfWork = new CountingUnitOfWork();

        await AuthzUserLifecyclePermissionsProjectionConsumer.Handle(
            new UserReactivatedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = projection.UserId,
                Email = "back@example.com",
                ActorType = "TenantEmployee",
            },
            new FakeRepository(projection),
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.True(projection.IsActive);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Missing_projection_is_a_noop()
    {
        var unitOfWork = new CountingUnitOfWork();

        await AuthzUserLifecyclePermissionsProjectionConsumer.Handle(
            Deactivated(Guid.NewGuid()),
            new FakeRepository(),
            unitOfWork,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static UserDeactivatedIntegrationEvent Deactivated(Guid userId) =>
        new()
        {
            TenantId = TenantId,
            UserId = userId,
            Email = "paused@example.com",
            ActorType = "TenantEmployee",
        };

    private static AuthzUserPermissionsProjection Projection() =>
        AuthzUserPermissionsProjection.Create(TenantId, Guid.NewGuid(), 1, ["notification.template.view"], []);

    private sealed class FakeRepository(params AuthzUserPermissionsProjection[] seed)
        : IAuthzUserPermissionsProjectionRepository
    {
        public Task<AuthzUserPermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(seed.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));

        public Task AddAsync(AuthzUserPermissionsProjection projection, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthzUserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
