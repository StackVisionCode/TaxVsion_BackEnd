using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Permissions.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Permissions;

/// <summary>
/// The RBAC deny layer's write path: <see cref="SetUserPermissionOverridesHandler"/> replaces a user's
/// per-user deny set and re-publishes the recomputed effective codes on
/// <c>UserRolesChangedIntegrationEvent</c> (no new event, no downstream change). Guards proven here:
/// deny subtracts from the published set; an empty set restores it; anti self-lockout (an admin can
/// never touch their own denies); actor-type coherence; tenant isolation; and perm_v bumped exactly once.
/// </summary>
public sealed class SetUserPermissionOverridesHandlerTests
{
    private static User Staff(Guid tenantId) =>
        User.Register(tenantId, "Amanda", "Reyes", "amanda@acme.com", "hash", UserActorType.TenantEmployee).Value;

    private static (Permission View, Permission Manage, Role Role) RoleGrantingTwo(Guid tenantId)
    {
        var view = Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "desc");
        var manage = Permission.Seed(Guid.NewGuid(), "documents.manage", "documents", "desc");
        var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        role.SetPermissions([view.Id, manage.Id], seeding: true);
        return (view, manage, role);
    }

    [Fact]
    public async Task Deny_subtracts_the_code_from_the_published_set_and_bumps_the_version_exactly_once()
    {
        var tenantId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var (view, manage, role) = RoleGrantingTwo(tenantId);
        var target = Staff(tenantId);
        var versionBefore = target.PermissionsVersion;

        var users = new FakeUserRepository { Seeded = target };
        var roles = new FakeRoleRepository { Catalog = [view, manage], UserRoles = [role] };
        var audit = new FakeAuthAuditWriter();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await SetUserPermissionOverridesHandler.Handle(
            new SetUserPermissionOverridesCommand(tenantId, target.Id, [manage.Id], admin),
            users,
            roles,
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        // The deny set was replaced with exactly the requested id.
        Assert.NotNull(roles.ReceivedDenies);
        Assert.Equal(new[] { manage.Id }, roles.ReceivedDenies);
        Assert.Equal(admin, roles.ReceivedDeniedBy);

        // The published effective set = roles − denies.
        var evt = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Contains("customers.view", evt.PermissionCodes);
        Assert.DoesNotContain("documents.manage", evt.PermissionCodes);
        Assert.Contains(role.Id, evt.RoleIds);
        Assert.Equal(UserActorType.TenantEmployee.ToString(), evt.ActorType);

        // perm_v bumped exactly once, and the event carries the new version.
        Assert.Equal(versionBefore + 1, target.PermissionsVersion);
        Assert.Equal(versionBefore + 1, evt.PermissionsVersion);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        // Audit records the dedicated action and the exact code denied (the "what"), for whom.
        Assert.NotNull(audit.Written);
        Assert.Equal(AuthAuditAction.UserPermissionOverridesChanged, audit.Written!.Action);
        Assert.Equal(target.Id, audit.Written.TargetId);
        Assert.Contains("documents.manage", audit.Written.DetailsJson);
    }

    [Fact]
    public async Task An_empty_deny_set_publishes_the_full_effective_set_again()
    {
        var tenantId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var (view, manage, role) = RoleGrantingTwo(tenantId);
        var target = Staff(tenantId);

        var roles = new FakeRoleRepository { Catalog = [view, manage], UserRoles = [role] };
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetUserPermissionOverridesHandler.Handle(
            new SetUserPermissionOverridesCommand(tenantId, target.Id, [], admin),
            new FakeUserRepository { Seeded = target },
            roles,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(roles.ReceivedDenies);
        Assert.Empty(roles.ReceivedDenies);

        var evt = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Contains("customers.view", evt.PermissionCodes);
        Assert.Contains("documents.manage", evt.PermissionCodes);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_changing_your_own_overrides_so_the_tenant_can_never_be_locked_out()
    {
        var tenantId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetUserPermissionOverridesHandler.Handle(
            new SetUserPermissionOverridesCommand(tenantId, admin, [Guid.NewGuid()], admin),
            new FakeUserRepository { Seeded = null },
            new FakeRoleRepository(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.SelfAction", result.Error.Code);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_a_target_in_a_different_tenant()
    {
        var tenantId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var target = Staff(Guid.NewGuid()); // a different tenant
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetUserPermissionOverridesHandler.Handle(
            new SetUserPermissionOverridesCommand(tenantId, target.Id, [], admin),
            new FakeUserRepository { Seeded = target },
            new FakeRoleRepository(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotFound", result.Error.Code);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_a_deny_that_does_not_fit_the_target_actor_type()
    {
        var tenantId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        // A customer-portal-only permission can neither be carried nor denied on a staff seat.
        var portalOnly = Permission.Seed(
            Guid.NewGuid(),
            "portal.documents.view",
            "portal",
            "desc",
            isCustomerPortal: true
        );
        var target = Staff(tenantId);
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetUserPermissionOverridesHandler.Handle(
            new SetUserPermissionOverridesCommand(tenantId, target.Id, [portalOnly.Id], admin),
            new FakeUserRepository { Seeded = target },
            new FakeRoleRepository { Catalog = [portalOnly], UserRoles = [] },
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", result.Error.Code);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    // -----------------------------------------------------------------------
    // Fakes (private to this file; FakeMessageBus is reused from the Application namespace).
    // -----------------------------------------------------------------------

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
        public IReadOnlyList<Guid>? ReceivedDenies { get; private set; }
        public Guid? ReceivedDeniedBy { get; private set; }

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(UserRoles);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> permissionIds,
            Guid? deniedByUserId,
            CancellationToken ct = default
        )
        {
            ReceivedDenies = permissionIds.ToList();
            ReceivedDeniedBy = deniedByUserId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

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

    private sealed class FakeAuthAuditWriter : IAuthAuditWriter
    {
        public AuthAuditLog? Written { get; private set; }

        public Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
        {
            Written = log;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRequestContext : IRequestContext
    {
        public string? IpAddress => "203.0.113.10";
        public string? UserAgent => "test-agent";
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
