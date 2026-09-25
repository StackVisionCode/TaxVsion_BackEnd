using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Consumers;
using TaxVision.Connectors.Domain.Permissions;

namespace TaxVision.Connectors.Tests.Authorization;

/// <summary>
/// Desactivar/retirar deja la proyección de permisos fail-closed; reactivar la vuelve a autorizar;
/// no-op si aún no existe. Fakes de mano, sin Moq: se llama <c>Handle(...)</c> directo.
/// </summary>
public sealed class UserLifecyclePermissionsProjectionConsumerTests
{
    [Fact]
    public async Task UserDeactivated_marks_the_projection_inactive_so_it_fails_closed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = UserPermissionsProjection.Create(tenantId, userId, 1, ["x"], []);
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = tenantId,
                UserId = userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.False((await repo.GetAsync(tenantId, userId))!.IsActive);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task UserOffboarded_marks_the_projection_inactive()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = UserPermissionsProjection.Create(tenantId, userId, 1, ["x"], []);
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = tenantId,
                UserId = userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = DateTime.UtcNow,
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.False((await repo.GetAsync(tenantId, userId))!.IsActive);
    }

    [Fact]
    public async Task UserReactivated_marks_the_projection_active_again()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = UserPermissionsProjection.Create(tenantId, userId, 1, ["x"], []);
        existing.MarkInactive();
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserReactivatedIntegrationEvent
            {
                TenantId = tenantId,
                UserId = userId,
                Email = "x@example.com",
                ActorType = "TenantEmployee",
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True((await repo.GetAsync(tenantId, userId))!.IsActive);
    }

    [Fact]
    public async Task UserDeactivated_is_a_noop_when_no_projection_exists()
    {
        var tenantId = Guid.NewGuid();
        var repo = new RecordingUserRepository();
        var uow = new NoOpUnitOfWork();

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = tenantId,
                UserId = Guid.NewGuid(),
                Email = "x@example.com",
                ActorType = "TenantEmployee",
            },
            repo,
            uow,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(0, uow.SaveCount);
    }

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class RecordingUserRepository(params UserPermissionsProjection[] seed)
        : IUserPermissionsProjectionRepository
    {
        private readonly Dictionary<(Guid TenantId, Guid UserId), UserPermissionsProjection> _byKey = seed.ToDictionary(
            p => (p.TenantId, p.UserId)
        );

        public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_byKey.GetValueOrDefault((tenantId, userId)));

        public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default)
        {
            _byKey[(projection.TenantId, projection.UserId)] = projection;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<UserPermissionsProjection>>(
                _byKey.Values.Where(p => p.TenantId == tenantId && p.IsActive && p.RoleIds().Contains(roleId)).ToList()
            );
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
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
