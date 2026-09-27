using BuildingBlocks.RateLimiting;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.RateLimiting.Abstractions;
using TaxVision.Auth.Application.Users.Queries;
using TaxVision.Auth.Domain.RateLimiting;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// A5 (A6.1 y A6.5 del plan) — el bootstrap único de acceso. Cubre la forma por actor type (el cliente
/// del portal nunca recibe semántica comercial), el estado coherente con el corte real de acceso, y el
/// ETag, que es lo que permite pedirlo en cada navegación sin costo.
/// </summary>
public sealed class GetMyAccessHandlerTests
{
    private const string StaffCode = "customers.view";
    private const string PortalCode = "portal.folders.view";

    private static readonly Guid TenantId = Guid.NewGuid();

    private static Permission StaffPermission { get; } =
        Permission.Seed(Guid.NewGuid(), StaffCode, "customers", "Ver clientes");

    private static Permission PortalPermission { get; } =
        Permission.Seed(Guid.NewGuid(), PortalCode, "portal", "Portal docs", isCustomerPortal: true);

    private static Permission BillingManage { get; } =
        Permission.Seed(
            Guid.NewGuid(),
            PermissionCatalog.BillingManage,
            "billing",
            "Gestionar el pago",
            isAssignableByTenant: false,
            isDangerous: true
        );

    private static User StaffUser(UserActorType actorType = UserActorType.TenantEmployee) =>
        User.Register(TenantId, "Ana", "Ruiz", "ana@example.com", "hash", actorType).Value;

    private static User PortalUser() =>
        User.Register(
            TenantId,
            "Cliente",
            "Uno",
            "cliente@example.com",
            "hash",
            UserActorType.CustomerPortal,
            Guid.NewGuid()
        ).Value;

    private static Role RoleWith(string name, params Permission[] permissions)
    {
        var role = Role.Create(TenantId, name, null).Value;
        role.SetPermissions(permissions.Select(permission => permission.Id).ToList());
        return role;
    }

    private static async Task<MyAccessResponse> HandleAsync(
        User user,
        Role role,
        IReadOnlyList<Permission> catalog,
        string modulesJson = """["customers","documents"]""",
        bool tenantIsActive = true,
        bool billingBlocked = false,
        long revision = 7
    )
    {
        var result = await GetMyAccessHandler.Handle(
            new GetMyAccessQuery(user.Id),
            new StubUsers(user),
            new StubRoles(role, catalog),
            new StubTenants(tenantIsActive, billingBlocked),
            new StubPlanLimits(TenantPlanLimits.Create(TenantId, "pro", 10, 10, 0, modulesJson)),
            new StubPlanCodes(revision),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task A_staff_user_gets_permissions_modules_versions_and_the_commercial_state()
    {
        var user = StaffUser();
        var access = await HandleAsync(user, RoleWith("Staff", StaffPermission), [StaffPermission]);

        Assert.Equal("TenantEmployee", access.ActorType);
        Assert.Equal([StaffCode], access.EffectivePermissions);
        Assert.Equal(["customers", "documents"], access.Modules);
        Assert.Equal(user.PermissionsVersion, access.PermissionsVersion);
        Assert.Equal(7, access.EntitlementsRevision);
        Assert.NotNull(access.Subscription);
        Assert.Equal(GetMyAccessHandler.StateActive, access.Subscription!.State);
        Assert.False(access.Subscription.CanManageBilling);
        Assert.NotEmpty(access.ETag);
    }

    /// <summary>
    /// La forma del portal no lleva semántica comercial: un cliente de la oficina no tiene por qué
    /// enterarse de si la oficina está al día con su suscripción.
    /// </summary>
    [Fact]
    public async Task A_customer_portal_user_never_gets_the_subscription_block()
    {
        var access = await HandleAsync(PortalUser(), RoleWith("Portal", PortalPermission), [PortalPermission]);

        Assert.Equal("CustomerPortal", access.ActorType);
        Assert.Null(access.Subscription);
        Assert.Equal([PortalCode], access.EffectivePermissions);
    }

    [Fact]
    public async Task CanManageBilling_comes_from_the_effective_permissions_not_from_the_actor_type()
    {
        var admin = StaffUser(UserActorType.TenantAdmin);
        var access = await HandleAsync(
            admin,
            RoleWith("Admin", StaffPermission, BillingManage),
            [StaffPermission, BillingManage]
        );

        Assert.True(access.Subscription!.CanManageBilling);

        // El mismo actor type SIN el permiso no puede gestionar: ser TenantAdmin no alcanza.
        var withoutBilling = await HandleAsync(
            StaffUser(UserActorType.TenantAdmin),
            RoleWith("Admin sin billing", StaffPermission),
            [StaffPermission, BillingManage]
        );
        Assert.False(withoutBilling.Subscription!.CanManageBilling);
    }

    /// <summary>A6.5 — el estado que ve el frontend coincide con el corte real de acceso.</summary>
    [Theory]
    [InlineData(true, false, GetMyAccessHandler.StateActive)]
    [InlineData(true, true, GetMyAccessHandler.StateBillingBlocked)]
    [InlineData(false, false, GetMyAccessHandler.StateSuspended)]
    // La suspensión administrativa manda: es la más restrictiva y no se arregla pagando.
    [InlineData(false, true, GetMyAccessHandler.StateSuspended)]
    public async Task The_state_matches_the_real_access_cutoff(
        bool tenantIsActive,
        bool billingBlocked,
        string expected
    )
    {
        var access = await HandleAsync(
            StaffUser(),
            RoleWith("Staff", StaffPermission),
            [StaffPermission],
            tenantIsActive: tenantIsActive,
            billingBlocked: billingBlocked
        );

        Assert.Equal(expected, access.Subscription!.State);
    }

    [Fact]
    public async Task A_plan_suspended_for_billing_also_reads_as_blocked()
    {
        var user = StaffUser();
        var result = await GetMyAccessHandler.Handle(
            new GetMyAccessQuery(user.Id),
            new StubUsers(user),
            new StubRoles(RoleWith("Staff", StaffPermission), [StaffPermission]),
            new StubTenants(isActive: true, billingBlocked: false),
            new StubPlanLimits(SuspendedLimits()),
            new StubPlanCodes(1),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(GetMyAccessHandler.StateBillingBlocked, result.Value.Subscription!.State);
    }

    [Fact]
    public async Task The_etag_changes_when_the_access_changes_and_not_otherwise()
    {
        var user = StaffUser();
        var role = RoleWith("Staff", StaffPermission);

        var first = await HandleAsync(user, role, [StaffPermission]);
        var again = await HandleAsync(user, role, [StaffPermission]);
        Assert.Equal(first.ETag, again.ETag);

        // Un módulo nuevo mueve el ETag aunque perm_v no se mueva: es justo el caso que un ETag basado
        // solo en perm_v dejaría pasar (el sidebar se quedaría sin la sección nueva).
        var withNewModule = await HandleAsync(
            user,
            role,
            [StaffPermission],
            modulesJson: """["customers","documents","campaigns"]"""
        );
        Assert.NotEqual(first.ETag, withNewModule.ETag);

        var withNewRevision = await HandleAsync(user, role, [StaffPermission], revision: 8);
        Assert.NotEqual(first.ETag, withNewRevision.ETag);
    }

    /// <summary>§R.4.1 — la superficie entra en el ETag para que una caché intermedia no pueda servir
    /// la respuesta de una superficie a otra.</summary>
    [Fact]
    public void The_etag_is_scoped_to_the_surface()
    {
        var response = new MyAccessResponse("TenantAdmin", ["customers.view"], ["customers"], 3, 5, null);

        Assert.NotEqual(
            GetMyAccessHandler.ComputeETag(response, surface: null),
            GetMyAccessHandler.ComputeETag(response, surface: "account")
        );
    }

    [Fact]
    public async Task An_inactive_user_gets_no_bootstrap()
    {
        var user = StaffUser();
        user.Deactivate(DateTime.UtcNow);

        var result = await GetMyAccessHandler.Handle(
            new GetMyAccessQuery(user.Id),
            new StubUsers(user),
            new StubRoles(RoleWith("Staff", StaffPermission), [StaffPermission]),
            new StubTenants(true, false),
            new StubPlanLimits(null),
            new StubPlanCodes(0),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotFound", result.Error.Code);
    }

    private static TenantPlanLimits SuspendedLimits()
    {
        var limits = TenantPlanLimits.Create(TenantId, "pro", 10, 10, 0, "[]");
        limits.SetSuspendedForBilling(true);
        return limits;
    }

    // ---------------------------------------------------------------------
    // Dobles mínimos: solo los métodos que el handler usa.
    // ---------------------------------------------------------------------

    private sealed class StubUsers(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<User?>(user.Id == id ? user : null);

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

    private sealed class StubRoles(Role role, IReadOnlyList<Permission> catalog) : IRoleRepository
    {
        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>([role]);

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(catalog);

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

        /// <summary>La unión efectiva la resuelve la base en producción; acá se compone del rol y el
        /// catálogo, que es lo mismo que devuelve el repositorio real sin denies.</summary>
        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(Guid userId, CancellationToken ct = default)
        {
            var codesById = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
            IReadOnlyList<string> codes = role
                .Permissions.Where(link => codesById.ContainsKey(link.PermissionId))
                .Select(link => codesById[link.PermissionId])
                .ToList();
            return Task.FromResult(codes);
        }

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

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

    private sealed class StubTenants(bool isActive, bool billingBlocked) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
        {
            var tenant = Tenant.Register(TenantId, "Oficina", "oficina", TenantKind.Customer, "UTC").Value;
            tenant.SetActive(isActive);
            tenant.SetBillingAccess(billingBlocked, billingBlocked ? "Expired" : null);
            return Task.FromResult<Tenant?>(tenant);
        }

        public Task<Tenant?> GetBySubDomainAsync(string subDomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Tenant tenant, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class StubPlanLimits(TenantPlanLimits? limits) : ITenantPlanLimitsStore
    {
        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(limits);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubPlanCodes(long revision) : ITenantPlanCodeProjectionRepository
    {
        public Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantPlanCodeProjection?>(TenantPlanCodeProjection.Create(tenantId, "pro", revision));

        public Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) => Task.CompletedTask;
    }
}
