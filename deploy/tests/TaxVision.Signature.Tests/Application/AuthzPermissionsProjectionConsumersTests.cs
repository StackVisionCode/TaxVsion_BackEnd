using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Consumers;
using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Tests.Application;

// RBAC Fase 7 -- consumers de la proyeccion de AUTORIZACION (distinta y deliberadamente
// independiente del consumer preexistente UserRolesChangedConsumer que alimenta la proyeccion de
// auditoria homonima). Mismo patron que CloudStorage/Customer/Postmaster/etc.
public sealed class AuthzPermissionsProjectionConsumersTests
{
    // -------------------- AuthzUserRolesChangedPermissionsProjectionConsumer --------------------

    [Fact]
    public async Task Creates_a_new_projection_when_none_exists_yet()
    {
        var repository = new FakeUserRepository();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 1,
            RoleNames = ["TenantAdmin"],
            RoleIds = [Guid.NewGuid()],
            PermissionCodes = ["signature.request.view"],
        };

        await AuthzUserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repository,
            new FakeUnitOfWork(),
            new CorrelationContext(),
            NullLogger<AuthzUserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetAsync(tenantId, userId);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.PermissionsVersion);
        Assert.Equal(["signature.request.view"], stored.PermissionCodes());
    }

    [Fact]
    public async Task Applies_newer_version_over_an_existing_projection()
    {
        var repository = new FakeUserRepository();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = AuthzUserPermissionsProjection.Create(tenantId, userId, 1, ["old"], []);
        await repository.AddAsync(existing);

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 2,
            RoleNames = [],
            RoleIds = [],
            PermissionCodes = ["new"],
        };

        await AuthzUserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repository,
            new FakeUnitOfWork(),
            new CorrelationContext(),
            NullLogger<AuthzUserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repository.GetAsync(tenantId, userId);
        Assert.Equal(2, stored!.PermissionsVersion);
        Assert.Equal(["new"], stored.PermissionCodes());
    }

    // -------------------- AuthzRolePermissionsChangedPermissionsProjectionConsumer --------------------

    [Fact]
    public async Task Recomputes_the_permission_union_for_a_multi_role_user()
    {
        var roleRepository = new FakeRoleRepository();
        var userRepository = new FakeUserRepository();
        var tenantId = Guid.NewGuid();
        var changedRoleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();

        // The user has TWO roles: one about to change, one untouched -- the union must keep
        // the untouched role's permissions after the recompute.
        await roleRepository.AddAsync(
            AuthzRolePermissionsProjection.Create(tenantId, otherRoleId, "Other", 1, ["other.permission"])
        );
        var userId = Guid.NewGuid();
        var userProjection = AuthzUserPermissionsProjection.Create(
            tenantId,
            userId,
            permissionsVersion: 1,
            permissionCodes: ["old.permission", "other.permission"],
            roleIds: [changedRoleId, otherRoleId]
        );
        await userRepository.AddAsync(userProjection);

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = changedRoleId,
            RoleName = "Changed",
            PermissionsVersion = 1,
            PermissionCodes = ["new.permission"],
        };

        await AuthzRolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepository,
            userRepository,
            new FakeUnitOfWork(),
            new CorrelationContext(),
            NullLogger<AuthzRolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var storedUser = await userRepository.GetAsync(tenantId, userId);
        Assert.Equal(new[] { "new.permission", "other.permission" }, storedUser!.PermissionCodes().OrderBy(c => c));
        // The user's own PermissionsVersion is untouched -- this change did not come from a
        // role reassignment of THIS user.
        Assert.Equal(1, storedUser.PermissionsVersion);
    }

    [Fact]
    public async Task Does_nothing_to_users_when_no_active_user_has_the_changed_role()
    {
        var roleRepository = new FakeRoleRepository();
        var userRepository = new FakeUserRepository();
        var tenantId = Guid.NewGuid();

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = Guid.NewGuid(),
            RoleName = "Orphan",
            PermissionsVersion = 1,
            PermissionCodes = ["x"],
        };

        await AuthzRolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepository,
            userRepository,
            new FakeUnitOfWork(),
            new CorrelationContext(),
            NullLogger<AuthzRolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var storedRole = await roleRepository.GetAsync(tenantId, evt.RoleId);
        Assert.NotNull(storedRole);
        Assert.Equal(["x"], storedRole!.PermissionCodes());
    }

    // -------------------- Test doubles --------------------

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeUserRepository : IAuthzUserPermissionsProjectionRepository
    {
        private readonly List<AuthzUserPermissionsProjection> _items = [];

        public Task<AuthzUserPermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(_items.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));

        public Task AddAsync(AuthzUserPermissionsProjection projection, CancellationToken ct = default)
        {
            _items.Add(projection);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthzUserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<AuthzUserPermissionsProjection>>(
                _items.Where(p => p.TenantId == tenantId && p.IsActive && p.RoleIds().Contains(roleId)).ToList()
            );
    }

    private sealed class FakeRoleRepository : IAuthzRolePermissionsProjectionRepository
    {
        private readonly List<AuthzRolePermissionsProjection> _items = [];

        public Task<AuthzRolePermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult(_items.FirstOrDefault(p => p.TenantId == tenantId && p.Id == roleId));

        public Task AddAsync(AuthzRolePermissionsProjection projection, CancellationToken ct = default)
        {
            _items.Add(projection);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthzRolePermissionsProjection>> FindByRoleIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<AuthzRolePermissionsProjection>>(
                _items.Where(p => p.TenantId == tenantId && roleIds.Contains(p.Id)).ToList()
            );
    }
}
