using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// El fan-out por titular es lo que hace verdadera la regla "roles − denies" fuera de Auth. Antes,
/// cambiar los permisos de un rol solo publicaba <c>RolePermissionsChanged</c>, y cada proyección
/// local recomponía la unión de los roles del usuario sin conocer sus denies: el permiso denegado
/// reaparecía (G3 de la auditoría).
/// </summary>
public sealed class RolePermissionsFanOutTests
{
    private static readonly Permission View = Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "desc");
    private static readonly Permission Manage = Permission.Seed(
        Guid.NewGuid(),
        "customers.manage",
        "customers",
        "desc"
    );
    private static readonly Permission Notes = Permission.Seed(Guid.NewGuid(), "notes.read", "planner", "desc");

    private static readonly IReadOnlyList<Permission> Catalog = [View, Manage, Notes];

    private static User Employee(Guid tenantId) =>
        User.Register(tenantId, "Amanda", "Reyes", "amanda@example.com", "hash", UserActorType.TenantEmployee).Value;

    private static Role RoleWith(Guid tenantId, string name, params Guid[] permissionIds)
    {
        var role = Role.Create(tenantId, name, null).Value;
        role.SetPermissions(permissionIds);
        return role;
    }

    [Fact]
    public async Task A_denied_permission_does_not_come_back_when_the_role_changes()
    {
        var tenantId = Guid.NewGuid();
        var holder = Employee(tenantId);
        var role = RoleWith(tenantId, "Editor", View.Id, Manage.Id);

        var users = new FanOutUserRepository([holder]);
        var roles = new FanOutRoleRepository(
            rolesByUser: new() { [holder.Id] = [role] },
            deniesByUser: new() { [holder.Id] = [Manage.Id] }
        );
        var bus = new FakeMessageBus();

        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            tenantId,
            role.Id,
            Catalog,
            users,
            roles,
            bus,
            "corr",
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(holder.Id, published.UserId);
        Assert.Contains("customers.view", published.PermissionCodes);
        Assert.DoesNotContain("customers.manage", published.PermissionCodes);
    }

    [Fact]
    public async Task A_multi_role_holder_keeps_the_permissions_of_the_role_that_did_not_change()
    {
        var tenantId = Guid.NewGuid();
        var holder = Employee(tenantId);
        var changed = RoleWith(tenantId, "Editor", Manage.Id);
        var other = RoleWith(tenantId, "Lector de notas", Notes.Id);

        var users = new FanOutUserRepository([holder]);
        var roles = new FanOutRoleRepository(new() { [holder.Id] = [changed, other] }, new());
        var bus = new FakeMessageBus();

        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            tenantId,
            changed.Id,
            Catalog,
            users,
            roles,
            bus,
            "corr",
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(["customers.manage", "notes.read"], published.PermissionCodes.Order());
        Assert.Equal(2, published.RoleIds.Length);
    }

    [Fact]
    public async Task A_deactivated_role_stops_granting_its_permissions()
    {
        var tenantId = Guid.NewGuid();
        var holder = Employee(tenantId);
        var deactivated = RoleWith(tenantId, "Editor", Manage.Id);
        deactivated.Deactivate();
        var active = RoleWith(tenantId, "Lector de notas", Notes.Id);

        var users = new FanOutUserRepository([holder]);
        var roles = new FanOutRoleRepository(new() { [holder.Id] = [deactivated, active] }, new());
        var bus = new FakeMessageBus();

        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            tenantId,
            deactivated.Id,
            Catalog,
            users,
            roles,
            bus,
            "corr",
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(["notes.read"], published.PermissionCodes);
        Assert.DoesNotContain("customers.manage", published.PermissionCodes);
    }

    [Fact]
    public async Task A_holder_whose_only_role_grants_nothing_ends_up_with_no_permissions()
    {
        var tenantId = Guid.NewGuid();
        var holder = Employee(tenantId);
        var empty = RoleWith(tenantId, "Rol vacío");

        var users = new FanOutUserRepository([holder]);
        var roles = new FanOutRoleRepository(new() { [holder.Id] = [empty] }, new());
        var bus = new FakeMessageBus();

        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            tenantId,
            empty.Id,
            Catalog,
            users,
            roles,
            bus,
            "corr",
            CancellationToken.None
        );

        var published = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Empty(published.PermissionCodes);
    }

    [Fact]
    public async Task Every_holder_gets_its_own_event_with_its_own_bumped_version()
    {
        var tenantId = Guid.NewGuid();
        var first = Employee(tenantId);
        var second = Employee(tenantId);
        var role = RoleWith(tenantId, "Editor", View.Id);
        var versionBefore = first.PermissionsVersion;

        var users = new FanOutUserRepository([first, second]);
        var roles = new FanOutRoleRepository(
            new() { [first.Id] = [role], [second.Id] = [role] },
            new() { [second.Id] = [View.Id] }
        );
        var bus = new FakeMessageBus();

        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            tenantId,
            role.Id,
            Catalog,
            users,
            roles,
            bus,
            "corr",
            CancellationToken.None
        );

        var published = bus.Published.OfType<UserRolesChangedIntegrationEvent>().ToList();
        Assert.Equal(2, published.Count);
        Assert.Equal(versionBefore + 1, first.PermissionsVersion);
        Assert.Equal(versionBefore + 1, second.PermissionsVersion);
        Assert.Contains("customers.view", published.Single(e => e.UserId == first.Id).PermissionCodes);
        // Al segundo se le denegó justo el permiso del rol: se queda sin ninguno.
        Assert.Empty(published.Single(e => e.UserId == second.Id).PermissionCodes);
    }

    [Fact]
    public async Task A_role_with_no_holders_publishes_nothing()
    {
        var bus = new FakeMessageBus();

        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Catalog,
            new FanOutUserRepository([]),
            new FanOutRoleRepository(new(), new()),
            bus,
            "corr",
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    private sealed class FanOutUserRepository(IReadOnlyList<User> holders) : IUserRepository
    {
        public Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult(holders);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(holders.FirstOrDefault(user => user.Id == id));

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FanOutRoleRepository(
        Dictionary<Guid, IReadOnlyList<Role>> rolesByUser,
        Dictionary<Guid, IReadOnlyList<Guid>> deniesByUser
    ) : IRoleRepository
    {
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>> GetRolesByUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>>(rolesByUser);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetDeniedPermissionIdsByUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(deniesByUser);

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
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

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<PermissionDenyInput> denies,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
