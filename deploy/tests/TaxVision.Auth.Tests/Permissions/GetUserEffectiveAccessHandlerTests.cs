using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Permissions.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Permissions;

/// <summary>
/// The read side of the deny layer: <see cref="GetUserEffectiveAccessHandler"/> returns everything the
/// "Edit access" drawer needs in one tenant-isolated call — the target's role-granted permissions grouped
/// by module, each flagged if currently denied. Only role-granted permissions appear (an inert deny on a
/// permission no role grants would render a meaningless toggle).
/// </summary>
public sealed class GetUserEffectiveAccessHandlerTests
{
    [Fact]
    public async Task Groups_role_granted_permissions_by_module_and_flags_the_denied_ones()
    {
        var tenantId = Guid.NewGuid();
        var view = Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "View customers");
        var manage = Permission.Seed(Guid.NewGuid(), "documents.manage", "documents", "Manage documents");
        var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        role.SetPermissions([view.Id, manage.Id], seeding: true);
        var target = User.Register(
            tenantId,
            "Amanda",
            "Reyes",
            "amanda@acme.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        target.BumpPermissionsVersion();

        var roles = new FakeRoleRepository
        {
            Catalog = [view, manage],
            UserRoles = [role],
            Denies = [manage.Id],
        };

        var result = await GetUserEffectiveAccessHandler.Handle(
            new GetUserEffectiveAccessQuery(tenantId, target.Id),
            new FakeUserRepository { Seeded = target },
            roles,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var response = result.Value;
        Assert.Equal(target.Id, response.UserId);
        Assert.Equal(UserActorType.TenantEmployee.ToString(), response.ActorType);
        Assert.Equal(target.PermissionsVersion, response.PermissionsVersion);
        Assert.Contains(Role.SystemTenantAdmin, response.Roles);

        // Two modules, alphabetically ordered, one permission each.
        Assert.Equal(["customers", "documents"], response.Modules.Select(module => module.Module));

        var viewAccess = response.Modules.Single(module => module.Module == "customers").Permissions.Single();
        Assert.Equal(view.Id, viewAccess.PermissionId);
        Assert.False(viewAccess.Denied);

        var manageAccess = response.Modules.Single(module => module.Module == "documents").Permissions.Single();
        Assert.Equal(manage.Id, manageAccess.PermissionId);
        Assert.True(manageAccess.Denied);
    }

    [Fact]
    public async Task Omits_a_permission_no_role_grants_even_when_it_exists_in_the_catalog()
    {
        var tenantId = Guid.NewGuid();
        var view = Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "View customers");
        var notGranted = Permission.Seed(Guid.NewGuid(), "tasks.delete", "tasks", "Delete tasks");
        var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        role.SetPermissions([view.Id], seeding: true);
        var target = User.Register(
            tenantId,
            "Amanda",
            "Reyes",
            "amanda@acme.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

        var result = await GetUserEffectiveAccessHandler.Handle(
            new GetUserEffectiveAccessQuery(tenantId, target.Id),
            new FakeUserRepository { Seeded = target },
            new FakeRoleRepository { Catalog = [view, notGranted], UserRoles = [role] },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.DoesNotContain("tasks", result.Value.Modules.Select(module => module.Module));
    }

    [Fact]
    public async Task Rejects_a_target_in_a_different_tenant()
    {
        var target = User.Register(
            Guid.NewGuid(),
            "Amanda",
            "Reyes",
            "amanda@acme.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

        var result = await GetUserEffectiveAccessHandler.Handle(
            new GetUserEffectiveAccessQuery(Guid.NewGuid(), target.Id), // a different tenant
            new FakeUserRepository { Seeded = target },
            new FakeRoleRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotFound", result.Error.Code);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? Seeded { get; set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Seeded is not null && Seeded.Id == id ? Seeded : null);

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

    private sealed class FakeRoleRepository : IRoleRepository
    {
        public IReadOnlyList<Permission> Catalog { get; init; } = [];
        public IReadOnlyList<Role> UserRoles { get; init; } = [];
        public IReadOnlyList<Guid> Denies { get; init; } = [];

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(UserRoles);

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Denies);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> permissionIds,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

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
            throw new NotSupportedException();

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
