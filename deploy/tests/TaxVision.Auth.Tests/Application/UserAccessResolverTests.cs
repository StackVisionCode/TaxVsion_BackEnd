using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// The single subtraction point of the RBAC deny layer:
/// <see cref="UserAccessResolver.ResolveEffectivePermissionCodes"/> computes
/// <c>effective = ⋃(role permissions) − denies</c>. The write-side commands (assign roles, set
/// overrides, create owner, accept invitation) all publish the codes this method returns, so the
/// deny subtraction must live here and nowhere else. A deny always wins; deny-only (no grants).
/// </summary>
public sealed class UserAccessResolverTests
{
    private static (Permission View, Permission Manage, Role Role) OneRoleWithTwoPermissions()
    {
        var view = Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "desc");
        var manage = Permission.Seed(Guid.NewGuid(), "documents.manage", "documents", "desc");
        var role = Role.Create(Guid.NewGuid(), "Reviewer", null, isSystem: false).Value;
        role.SetPermissions([view.Id, manage.Id], seeding: true);
        return (view, manage, role);
    }

    [Fact]
    public void Returns_the_full_union_when_there_are_no_denies()
    {
        var (view, manage, role) = OneRoleWithTwoPermissions();

        var codes = UserAccessResolver.ResolveEffectivePermissionCodes([role], [view, manage]);

        Assert.Equal(2, codes.Length);
        Assert.Contains("customers.view", codes);
        Assert.Contains("documents.manage", codes);
    }

    [Fact]
    public void Subtracts_a_denied_permission_id_from_the_effective_set()
    {
        var (view, manage, role) = OneRoleWithTwoPermissions();

        var codes = UserAccessResolver.ResolveEffectivePermissionCodes([role], [view, manage], [manage.Id]);

        Assert.Single(codes);
        Assert.Contains("customers.view", codes);
        Assert.DoesNotContain("documents.manage", codes);
    }

    [Fact]
    public void An_empty_deny_set_restores_the_full_effective_set()
    {
        var (view, manage, role) = OneRoleWithTwoPermissions();

        // Deny, then undo (an empty set is how the command removes every override).
        var denied = UserAccessResolver.ResolveEffectivePermissionCodes([role], [view, manage], [manage.Id]);
        var restored = UserAccessResolver.ResolveEffectivePermissionCodes([role], [view, manage], []);

        Assert.Single(denied);
        Assert.Equal(2, restored.Length);
        Assert.Contains("documents.manage", restored);
    }

    [Fact]
    public void A_deny_on_a_permission_no_role_grants_is_a_no_op()
    {
        var (view, manage, role) = OneRoleWithTwoPermissions();
        var unrelated = Permission.Seed(Guid.NewGuid(), "tasks.delete", "tasks", "desc");

        var codes = UserAccessResolver.ResolveEffectivePermissionCodes(
            [role],
            [view, manage, unrelated],
            [unrelated.Id]
        );

        Assert.Equal(2, codes.Length);
        Assert.Contains("customers.view", codes);
        Assert.Contains("documents.manage", codes);
    }

    // ResolveAsync (the /auth/me read path): the pre-RBAC defaults fallback must not undo the deny layer.

    [Fact]
    public async Task ResolveAsync_keeps_the_set_empty_when_a_user_with_roles_has_everything_denied()
    {
        var user = User.Register(
            Guid.NewGuid(),
            "Ada",
            "Lovelace",
            "ada@acme.com",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        var role = Role.Create(user.TenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        // The user HAS an active role, but the DB-computed effective set (roles − denies) is empty.
        var roles = new FakeResolverRoleRepository { UserRoles = [role], EffectiveCodes = [] };

        var (_, permissions) = await UserAccessResolver.ResolveAsync(user, roles);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_actor_defaults_only_when_the_user_has_no_active_roles()
    {
        var user = User.Register(
            Guid.NewGuid(),
            "Grace",
            "Hopper",
            "grace@acme.com",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        var roles = new FakeResolverRoleRepository { UserRoles = [], EffectiveCodes = [] };

        var (_, permissions) = await UserAccessResolver.ResolveAsync(user, roles);

        var expected = PermissionCatalog
            .DefaultsFor(UserActorType.TenantAdmin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code);
        Assert.NotEmpty(permissions);
        Assert.Equal(expected, permissions.OrderBy(code => code));
    }

    private sealed class FakeResolverRoleRepository : IRoleRepository
    {
        public IReadOnlyList<Role> UserRoles { get; init; } = [];
        public IReadOnlyList<string> EffectiveCodes { get; init; } = [];

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(UserRoles);

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(EffectiveCodes);

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

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> permissionIds,
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
