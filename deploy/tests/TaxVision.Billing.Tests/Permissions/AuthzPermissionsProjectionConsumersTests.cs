using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Common;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Permissions.Consumers;
using TaxVision.Billing.Domain.Permissions;
using Xunit;

namespace TaxVision.Billing.Tests.Permissions;

// H-01 — Billing era el único de los 17 servicios sin proyección local de permisos, así que
// [HasPermission("billing.*")] no tenía contra qué resolver. Mismo patrón que Signature/Documents.
public sealed class AuthzPermissionsProjectionConsumersTests
{
    [Fact]
    public async Task Crea_la_proyeccion_cuando_el_usuario_todavia_no_tiene_una()
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
            PermissionCodes = ["billing.view"],
            ActorType = "TenantAdmin",
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
        Assert.Equal(["billing.view"], stored.PermissionCodes());
    }

    [Fact]
    public async Task Ignora_un_evento_con_version_anterior_a_la_ya_proyectada()
    {
        var repository = new FakeUserRepository();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await repository.AddAsync(AuthzUserPermissionsProjection.Create(tenantId, userId, 5, ["billing.manage"], []));

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 3,
            RoleNames = [],
            RoleIds = [],
            PermissionCodes = [],
            ActorType = "TenantAdmin",
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
        Assert.Equal(5, stored!.PermissionsVersion);
        Assert.Equal(["billing.manage"], stored.PermissionCodes());
    }

    [Fact]
    public async Task Recompone_la_union_de_permisos_de_un_usuario_multi_rol()
    {
        var roleRepository = new FakeRoleRepository();
        var userRepository = new FakeUserRepository();
        var tenantId = Guid.NewGuid();
        var changedRoleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();

        // El usuario tiene DOS roles: solo cambia uno — el otro no puede perder sus permisos.
        await roleRepository.AddAsync(
            AuthzRolePermissionsProjection.Create(tenantId, otherRoleId, "Otro", 1, ["billing.view"])
        );
        var userId = Guid.NewGuid();
        await userRepository.AddAsync(
            AuthzUserPermissionsProjection.Create(
                tenantId,
                userId,
                permissionsVersion: 1,
                permissionCodes: ["viejo", "billing.view"],
                roleIds: [changedRoleId, otherRoleId]
            )
        );

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = changedRoleId,
            RoleName = "Cambiado",
            PermissionsVersion = 1,
            PermissionCodes = ["billing.manage"],
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
        Assert.Equal(new[] { "billing.manage", "billing.view" }, storedUser!.PermissionCodes().OrderBy(code => code));
        // Cambió el rol, no la asignación de roles de ESTE usuario: su versión no se toca.
        Assert.Equal(1, storedUser.PermissionsVersion);
    }

    [Fact]
    public async Task Guarda_el_rol_aunque_ningun_usuario_activo_lo_tenga()
    {
        var roleRepository = new FakeRoleRepository();
        var tenantId = Guid.NewGuid();
        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = Guid.NewGuid(),
            RoleName = "Huerfano",
            PermissionsVersion = 1,
            PermissionCodes = ["billing.view"],
        };

        await AuthzRolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepository,
            new FakeUserRepository(),
            new FakeUnitOfWork(),
            new CorrelationContext(),
            NullLogger<AuthzRolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var storedRole = await roleRepository.GetAsync(tenantId, evt.RoleId);
        Assert.NotNull(storedRole);
        Assert.Equal(["billing.view"], storedRole!.PermissionCodes());
    }

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
