using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Roles.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// A4.2 / A4.3 — el catálogo con las banderas del techo y el flag <c>grantable</c> del tenant que
/// consulta, y la lista de titulares de un rol. Sin el primero el picker de la UI solo podía
/// descubrir el techo con un 400 al guardar; sin el segundo desactivar un rol era a ciegas.
/// </summary>
public sealed class RoleCatalogQueriesTests
{
    private const string PortalCode = "portal.folders.view";
    private const string CampaignsCode = "campaigns.view";
    private const string DangerousCode = "roles.manage";
    private const string StaffCode = "customers.view";

    private static IReadOnlyList<Permission> Catalog() =>
        [
            Permission.Seed(Guid.NewGuid(), StaffCode, "customers", "Ver clientes"),
            Permission.Seed(Guid.NewGuid(), CampaignsCode, "campaigns", "Ver campañas", minPlanTier: (int)PlanTier.Pro),
            Permission.Seed(Guid.NewGuid(), PortalCode, "portal", "Portal docs", isCustomerPortal: true),
            Permission.Seed(
                Guid.NewGuid(),
                DangerousCode,
                "users",
                "Gestionar roles",
                isAssignableByTenant: false,
                isDangerous: true
            ),
        ];

    [Fact]
    public async Task The_catalog_exposes_the_ceiling_flags_and_marks_what_the_tenant_can_grant()
    {
        var tenantId = Guid.NewGuid();
        var roles = new StubRoleRepository { Catalog = Catalog() };
        var planLimits = new StubPlanLimitsStore
        {
            Limits = TenantPlanLimits.Create(tenantId, "starter", 5, 5, 0, "[]"),
        };

        var result = await GetPermissionsCatalogHandler.Handle(
            new GetPermissionsCatalogQuery(tenantId),
            roles,
            planLimits,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);

        var dangerous = result.Value.Single(permission => permission.Code == DangerousCode);
        Assert.False(dangerous.IsAssignableByTenant);
        Assert.True(dangerous.IsDangerous);
        Assert.False(dangerous.Grantable);

        var outsideTier = result.Value.Single(permission => permission.Code == CampaignsCode);
        Assert.Equal((int)PlanTier.Pro, outsideTier.MinPlanTier);
        Assert.False(outsideTier.Grantable);

        var staff = result.Value.Single(permission => permission.Code == StaffCode);
        Assert.True(staff.Grantable);
        Assert.Contains("TenantEmployee", staff.AllowedActorTypes);

        var portal = result.Value.Single(permission => permission.Code == PortalCode);
        Assert.Equal(["CustomerPortal"], portal.AllowedActorTypes);
    }

    [Fact]
    public async Task A_pro_tenant_can_grant_the_pro_tier_permission()
    {
        var tenantId = Guid.NewGuid();
        var roles = new StubRoleRepository { Catalog = Catalog() };
        var planLimits = new StubPlanLimitsStore
        {
            Limits = TenantPlanLimits.Create(tenantId, "pro", 5, 5, 0, """["campaigns","customers"]"""),
        };

        var result = await GetPermissionsCatalogHandler.Handle(
            new GetPermissionsCatalogQuery(tenantId),
            roles,
            planLimits,
            CancellationToken.None
        );

        Assert.True(result.Value.Single(permission => permission.Code == CampaignsCode).Grantable);
    }

    /// <summary>Un módulo que el plan no habilita deja la permission fuera del techo aunque el tier
    /// alcance — es la dimensión de entitlements de §27.</summary>
    [Fact]
    public async Task A_module_the_plan_does_not_enable_is_not_grantable()
    {
        var tenantId = Guid.NewGuid();
        var roles = new StubRoleRepository { Catalog = Catalog() };
        var planLimits = new StubPlanLimitsStore
        {
            Limits = TenantPlanLimits.Create(tenantId, "enterprise", 5, 5, 0, """["customers"]"""),
        };

        var result = await GetPermissionsCatalogHandler.Handle(
            new GetPermissionsCatalogQuery(tenantId),
            roles,
            planLimits,
            CancellationToken.None
        );

        Assert.False(result.Value.Single(permission => permission.Code == CampaignsCode).Grantable);
        Assert.True(result.Value.Single(permission => permission.Code == StaffCode).Grantable);
    }

    [Fact]
    public async Task Without_a_tenant_the_catalog_still_answers_with_the_hard_ceiling()
    {
        var roles = new StubRoleRepository { Catalog = Catalog() };

        var result = await GetPermissionsCatalogHandler.Handle(
            new GetPermissionsCatalogQuery(TenantId: null),
            roles,
            new StubPlanLimitsStore(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Single(permission => permission.Code == DangerousCode).Grantable);
    }

    [Fact]
    public async Task The_role_users_query_lists_the_active_holders()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Front desk", null).Value;
        var roles = new StubRoleRepository { Catalog = [], Role = role };
        var users = new StubUserRepository();
        users.Holders.Add(
            User.Register(tenantId, "Ana", "Ruiz", "ana@example.com", "hash", UserActorType.TenantEmployee).Value
        );

        var result = await GetRoleUsersHandler.Handle(
            new GetRoleUsersQuery(tenantId, role.Id),
            roles,
            users,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var holder = Assert.Single(result.Value);
        Assert.Equal("ana@example.com", holder.Email);
        Assert.Equal("TenantEmployee", holder.ActorType);
    }

    [Fact]
    public async Task The_role_users_query_never_crosses_the_tenant_boundary()
    {
        var role = Role.Create(Guid.NewGuid(), "Front desk", null).Value;
        var roles = new StubRoleRepository { Catalog = [], Role = role };

        var result = await GetRoleUsersHandler.Handle(
            new GetRoleUsersQuery(Guid.NewGuid(), role.Id),
            roles,
            new StubUserRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotFound", result.Error.Code);
    }

    private sealed class StubRoleRepository : IRoleRepository
    {
        public IReadOnlyList<Permission> Catalog { get; init; } = [];
        public Role? Role { get; init; }

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult(Role?.Id == roleId ? Role : null);

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
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<PermissionDenyInput> denies,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            EnsureSystemRolesAsync(tenantId, ct);

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPlanLimitsStore : ITenantPlanLimitsStore
    {
        public TenantPlanLimits? Limits { get; init; }

        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Limits);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public List<User> Holders { get; } = [];

        public Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>(Holders);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

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
}
