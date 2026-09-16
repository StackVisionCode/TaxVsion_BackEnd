using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Roles.Queries;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Application;

/// <summary>GET /auth/roles expone `AssignableActorTypes` por rol para que el picker del frontend no
/// ofrezca roles que el backend rechazaría: un rol es asignable a un actor type solo si TODOS sus
/// permisos lo permiten (misma regla que ActorTypeRoleGuard).</summary>
public sealed class GetRolesHandlerTests
{
    [Fact]
    public async Task Computes_assignable_actor_types_from_the_role_permissions()
    {
        var tenantId = Guid.NewGuid();
        var staffPermId = Guid.NewGuid();
        var portalPermId = Guid.NewGuid();

        // Permiso staff (inferencia default → TenantEmployee/TenantAdmin/PlatformAdmin) y permiso
        // de portal (isCustomerPortal → solo CustomerPortal).
        var staffPerm = Permission.Seed(staffPermId, "customers.view", "customers", "Ver clientes");
        var portalPerm = Permission.Seed(
            portalPermId,
            "portal.docs.view",
            "documents",
            "Portal docs",
            isCustomerPortal: true
        );

        var staffRole = Role.Create(tenantId, "Firm Staff", null).Value;
        staffRole.SetPermissions([staffPermId], seeding: true);
        var portalRole = Role.Create(tenantId, "Customer Portal", null, isSystem: true).Value;
        portalRole.SetPermissions([portalPermId], seeding: true);

        var repo = new FakeRoleRepository { TenantRoles = [staffRole, portalRole], Catalog = [staffPerm, portalPerm] };

        var result = await GetRolesHandler.Handle(new GetRolesQuery(tenantId), repo, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var staff = result.Value.Single(role => role.Name == "Firm Staff");
        Assert.Contains("TenantEmployee", staff.AssignableActorTypes);
        Assert.Contains("TenantAdmin", staff.AssignableActorTypes);
        Assert.DoesNotContain("CustomerPortal", staff.AssignableActorTypes);

        var portal = result.Value.Single(role => role.Name == "Customer Portal");
        Assert.Equal(["CustomerPortal"], portal.AssignableActorTypes);
    }

    [Fact]
    public async Task A_role_with_no_permissions_is_assignable_to_every_tenant_actor_type()
    {
        var tenantId = Guid.NewGuid();
        var emptyRole = Role.Create(tenantId, "Empty", null).Value;
        var repo = new FakeRoleRepository { TenantRoles = [emptyRole], Catalog = [] };

        var result = await GetRolesHandler.Handle(new GetRolesQuery(tenantId), repo, CancellationToken.None);

        var role = Assert.Single(result.Value);
        Assert.Contains("TenantEmployee", role.AssignableActorTypes);
        Assert.Contains("TenantAdmin", role.AssignableActorTypes);
        Assert.Contains("CustomerPortal", role.AssignableActorTypes);
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        public IReadOnlyList<Role> TenantRoles { get; init; } = [];
        public IReadOnlyList<Permission> Catalog { get; init; } = [];

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(TenantRoles);

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            EnsureSystemRolesAsync(tenantId, ct);

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
