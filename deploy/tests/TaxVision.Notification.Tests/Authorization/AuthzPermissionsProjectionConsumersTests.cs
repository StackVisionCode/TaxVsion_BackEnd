using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Authorization.Abstractions;
using TaxVision.Notification.Application.Authorization.Consumers;
using TaxVision.Notification.Domain.Authorization;

namespace TaxVision.Notification.Tests.Authorization;

/// <summary>
/// Tests unitarios de <c>AuthzPermissionsProjectionConsumers.cs</c> (RBAC Fase 7) — cierran el
/// gap real de que Notification nunca registraba ningún <c>IUserPermissionsSource</c> en DI
/// pese a ya usar <c>[HasPermission]</c> en ~28 acciones de controller. Mismo patrón que
/// <c>PostmasterOutboundEmailCallbackConsumersTests.cs</c>: fakes locales de mano, sin Moq —
/// llama <c>Handle(...)</c> directo con repos/UoW/correlation-context fake.
///
/// Deliberadamente separado de <c>PermissionsProjectionTests.cs</c>/su análogo de Fase 4 (fan-out
/// de notificaciones) — estos consumers escriben en tablas DISTINTAS
/// (AuthzUserPermissionsProjections/AuthzRolePermissionsProjections), no las que usa
/// <c>IRecipientResolver</c>.
/// </summary>
public sealed class AuthzPermissionsProjectionConsumersTests
{
    [Fact]
    public async Task UserRolesChanged_creates_projection_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var repo = new RecordingUserRepository();
        var uow = new NoOpUnitOfWork();

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 1,
            PermissionCodes = ["notification.template.view"],
            RoleIds = [roleId],
        };

        await AuthzUserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<AuthzUserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId, userId);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.PermissionsVersion);
        Assert.Equal(["notification.template.view"], stored.PermissionCodes());
        Assert.Equal([roleId], stored.RoleIds());
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task UserRolesChanged_applies_newer_version_to_existing_projection()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = AuthzUserPermissionsProjection.Create(tenantId, userId, 1, ["a"], []);
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 2,
            PermissionCodes = ["a", "b"],
            RoleIds = [],
        };

        await AuthzUserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<AuthzUserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId, userId);
        Assert.Equal(2, stored!.PermissionsVersion);
        Assert.Equal(["a", "b"], stored.PermissionCodes());
    }

    [Fact]
    public async Task UserRolesChanged_ignores_out_of_order_event()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = AuthzUserPermissionsProjection.Create(tenantId, userId, 5, ["a", "b"], []);
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 4,
            PermissionCodes = ["stale"],
            RoleIds = [],
        };

        await AuthzUserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<AuthzUserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId, userId);
        Assert.Equal(5, stored!.PermissionsVersion);
        Assert.Equal(["a", "b"], stored.PermissionCodes());
    }

    [Fact]
    public async Task RolePermissionsChanged_recomputes_union_for_multi_role_affected_user()
    {
        var tenantId = Guid.NewGuid();
        var changedRoleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // El usuario ya tenía otro rol cacheado con sus propios permisos.
        var otherRole = AuthzRolePermissionsProjection.Create(
            tenantId,
            otherRoleId,
            "Viewer",
            1,
            ["notification.log.view"]
        );
        var roleRepo = new RecordingRoleRepository(otherRole);

        var user = AuthzUserPermissionsProjection.Create(
            tenantId,
            userId,
            1,
            ["notification.log.view"],
            [changedRoleId, otherRoleId]
        );
        var userRepo = new RecordingUserRepository(user);
        var uow = new NoOpUnitOfWork();

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = changedRoleId,
            RoleName = "Editor",
            PermissionsVersion = 1,
            PermissionCodes = ["notification.template.edit"],
        };

        await AuthzRolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepo,
            userRepo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<AuthzRolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        // El nuevo rol se cacheó...
        var storedRole = await roleRepo.GetAsync(tenantId, changedRoleId);
        Assert.NotNull(storedRole);
        Assert.Equal(["notification.template.edit"], storedRole!.PermissionCodes());

        // ...y el usuario multi-rol conserva AMBOS conjuntos de permisos (unión), no solo el rol que cambió.
        var storedUser = await userRepo.GetAsync(tenantId, userId);
        Assert.NotNull(storedUser);
        Assert.Contains("notification.template.edit", storedUser!.PermissionCodes());
        Assert.Contains("notification.log.view", storedUser.PermissionCodes());
        Assert.Equal(1, storedUser.PermissionsVersion); // no cambia con RolePermissionsChanged
    }

    [Fact]
    public async Task RolePermissionsChanged_with_no_affected_users_still_saves_the_role_projection()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var roleRepo = new RecordingRoleRepository();
        var userRepo = new RecordingUserRepository(); // sin usuarios afectados
        var uow = new NoOpUnitOfWork();

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = roleId,
            RoleName = "Empty",
            PermissionsVersion = 1,
            PermissionCodes = ["notification.template.view"],
        };

        await AuthzRolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepo,
            userRepo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<AuthzRolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        Assert.NotNull(await roleRepo.GetAsync(tenantId, roleId));
        Assert.Equal(1, uow.SaveCount);
    }

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class RecordingUserRepository(params AuthzUserPermissionsProjection[] seed)
        : IAuthzUserPermissionsProjectionRepository
    {
        private readonly Dictionary<(Guid TenantId, Guid UserId), AuthzUserPermissionsProjection> _byKey =
            seed.ToDictionary(p => (p.TenantId, p.UserId));

        public Task<AuthzUserPermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(_byKey.GetValueOrDefault((tenantId, userId)));

        public Task AddAsync(AuthzUserPermissionsProjection projection, CancellationToken ct = default)
        {
            _byKey[(projection.TenantId, projection.UserId)] = projection;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthzUserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<AuthzUserPermissionsProjection>>(
                _byKey.Values.Where(p => p.TenantId == tenantId && p.IsActive && p.RoleIds().Contains(roleId)).ToList()
            );
    }

    private sealed class RecordingRoleRepository(params AuthzRolePermissionsProjection[] seed)
        : IAuthzRolePermissionsProjectionRepository
    {
        private readonly Dictionary<Guid, AuthzRolePermissionsProjection> _byId = seed.ToDictionary(p => p.Id);

        public Task<AuthzRolePermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult(_byId.TryGetValue(roleId, out var role) && role.TenantId == tenantId ? role : null);

        public Task AddAsync(AuthzRolePermissionsProjection projection, CancellationToken ct = default)
        {
            _byId[projection.Id] = projection;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthzRolePermissionsProjection>> FindByRoleIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<AuthzRolePermissionsProjection>>(
                _byId.Values.Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id)).ToList()
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
