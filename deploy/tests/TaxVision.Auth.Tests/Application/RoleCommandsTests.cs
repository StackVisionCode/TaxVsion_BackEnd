using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Roles.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// RBAC Fase 3 (RBAC_Hardening_Plan.md) — cubre el guardarraíl nuevo end-to-end a través de
/// <see cref="CreateRoleHandler"/>/<see cref="SetRolePermissionsHandler"/>, no solo la función
/// pura <see cref="TaxVision.Auth.Application.Common.ActorTypeRoleGuard"/> (ver
/// ActorTypeRoleGuardTests.cs para esa capa). Primeros tests de estos dos handlers en el repo —
/// fakes mínimos en memoria para las 6 dependencias, sin mocking framework, mismo criterio que
/// FakeMessageBus.
/// </summary>
public sealed class RoleCommandsTests
{
    /// <summary>Sin titulares salvo que el test siembre alguno (A4: el techo y el guard de actor
    /// type miran los actor types de los titulares del rol).</summary>
    private sealed class FakeUserRepository : IUserRepository
    {
        public List<User> Holders { get; } = [];

        public Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>(Holders);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => Task.FromResult<User?>(null);

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<User?>(null);

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(0);

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<User>, int)>(([], 0));
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly List<Role> _roles = [];
        public IReadOnlyList<Permission> Catalog { get; init; } = [];

        public void Seed(Role role) => _roles.Add(role);

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult(_roles.SingleOrDefault(r => r.Id == roleId));

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>(_roles.Where(r => r.TenantId == tenantId).ToList());

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Role>>(_roles.Where(r => roleIds.Contains(r.Id)).ToList());

        public Task AddAsync(Role role, CancellationToken ct = default)
        {
            _roles.Add(role);
            return Task.CompletedTask;
        }

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            Task.FromResult(_roles.Any(r => r.TenantId == tenantId && r.Name == name));

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            EnsureSystemRolesAsync(tenantId, ct);

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<PermissionDenyInput> denies,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            Task.FromResult<Role?>(null);
    }

    private sealed class FakeTenantPlanLimitsStore : ITenantPlanLimitsStore
    {
        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantPlanLimits?>(null);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAuthAuditWriter : IAuthAuditWriter
    {
        public Task AddAsync(AuthAuditLog log, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>RBAC Fase 10 — captura el log en vez de descartarlo, para AuthAuditLog_written_when_role_created.</summary>
    private sealed class SpyAuthAuditWriter : IAuthAuditWriter
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
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static Permission PortalPermission() =>
        Permission.Seed(Guid.NewGuid(), "portal.folders.view", "portal", "desc", isCustomerPortal: true);

    private static Permission StaffPermission() =>
        Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "desc", isCustomerPortal: false);

    /// <summary>Un permiso que el plan Starter del tenant de estos tests no incluye — el fake de
    /// límites devuelve null, que resuelve a Starter.</summary>
    private static Permission ProTierPermission() =>
        Permission.Seed(Guid.NewGuid(), "campaigns.view", "campaigns", "desc", minPlanTier: (int)PlanTier.Pro);

    private static User Holder(Guid tenantId, UserActorType actorType)
    {
        var user = User.Register(
            tenantId,
            "Ana",
            "Ruiz",
            actorType == UserActorType.CustomerPortal ? "cliente@example.com" : "staff@example.com",
            "hash",
            actorType,
            actorType == UserActorType.CustomerPortal ? Guid.NewGuid() : null
        ).Value;
        return user;
    }

    [Fact]
    public async Task CreateRoleHandler_rejects_customer_portal_only_permission_for_staff_role()
    {
        var portalPermission = PortalPermission();
        var roles = new FakeRoleRepository { Catalog = [portalPermission] };
        var tenantId = Guid.NewGuid();

        var command = new CreateRoleCommand(
            tenantId,
            Guid.NewGuid(),
            "Rol mezclado",
            null,
            [portalPermission.Id],
            UserActorType.TenantAdmin
        );

        var result = await CreateRoleHandler.Handle(
            command,
            roles,
            new FakeTenantPlanLimitsStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", result.Error.Code);
        Assert.Contains(portalPermission.Code, result.Error.Message);
    }

    [Fact]
    public async Task CreateRoleHandler_accepts_valid_staff_permissions()
    {
        var staffPermission = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [staffPermission] };
        var tenantId = Guid.NewGuid();

        var command = new CreateRoleCommand(
            tenantId,
            Guid.NewGuid(),
            "Rol de staff válido",
            null,
            [staffPermission.Id],
            UserActorType.TenantEmployee
        );

        var result = await CreateRoleHandler.Handle(
            command,
            roles,
            new FakeTenantPlanLimitsStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(staffPermission.Code, result.Value.PermissionCodes);
    }

    /// <summary>RBAC Fase 10 — AuthAuditLog_written_when_role_created.</summary>
    [Fact]
    public async Task AuthAuditLog_written_when_role_created()
    {
        var staffPermission = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [staffPermission] };
        var tenantId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var audit = new SpyAuthAuditWriter();

        var command = new CreateRoleCommand(
            tenantId,
            createdByUserId,
            "Rol auditado",
            null,
            [staffPermission.Id],
            UserActorType.TenantEmployee
        );

        var result = await CreateRoleHandler.Handle(
            command,
            roles,
            new FakeTenantPlanLimitsStore(),
            audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(audit.Written);
        Assert.Equal(AuthAuditAction.RoleCreated, audit.Written!.Action);
        Assert.Equal(tenantId, audit.Written.TenantId);
        Assert.Equal(createdByUserId, audit.Written.UserId);
        Assert.Equal("Role", audit.Written.TargetType);
        Assert.Equal(result.Value.Id, audit.Written.TargetId);
        Assert.True(audit.Written.Success);
    }

    [Fact]
    public async Task CreateRoleHandler_rejects_PlatformAdmin_target_when_caller_is_not_PlatformAdmin()
    {
        var staffPermission = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [staffPermission] };
        var tenantId = Guid.NewGuid();

        var command = new CreateRoleCommand(
            tenantId,
            Guid.NewGuid(),
            "Rol falso platform admin",
            null,
            [staffPermission.Id],
            UserActorType.PlatformAdmin,
            CallerIsPlatformAdmin: false
        );

        var result = await CreateRoleHandler.Handle(
            command,
            roles,
            new FakeTenantPlanLimitsStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.TargetActorTypeForbidden", result.Error.Code);
    }

    [Fact]
    public async Task CreateRoleHandler_allows_PlatformAdmin_target_when_caller_is_PlatformAdmin()
    {
        var staffPermission = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [staffPermission] };
        var tenantId = Guid.NewGuid();

        var command = new CreateRoleCommand(
            tenantId,
            Guid.NewGuid(),
            "Rol platform admin legítimo",
            null,
            [staffPermission.Id],
            UserActorType.PlatformAdmin,
            CallerIsPlatformAdmin: true
        );

        var result = await CreateRoleHandler.Handle(
            command,
            roles,
            new FakeTenantPlanLimitsStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task SetRolePermissionsHandler_rejects_customer_portal_only_permission_added_to_existing_role()
    {
        var portalPermission = PortalPermission();
        var roles = new FakeRoleRepository { Catalog = [portalPermission] };
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol custom existente", null).Value;
        roles.Seed(role);

        var command = new SetRolePermissionsCommand(tenantId, role.Id, Guid.NewGuid(), [portalPermission.Id]);

        var result = await SetRolePermissionsHandler.Handle(
            command,
            roles,
            new FakeUserRepository(),
            new FakeTenantPlanLimitsStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", result.Error.Code);
        Assert.Contains(portalPermission.Code, result.Error.Message);
        // El rol no debe haber quedado modificado — el guardarraíl corre ANTES de SetPermissions.
        Assert.Empty(role.Permissions);
    }

    // -----------------------------------------------------------------------
    // A4 — techo por delta, destino del rol, titulares, unicidad y reactivar
    // -----------------------------------------------------------------------

    private static async Task<Result> SetPermissionsAsync(
        FakeRoleRepository roles,
        FakeUserRepository users,
        Guid tenantId,
        Guid roleId,
        IReadOnlyList<Guid> permissionIds,
        IMessageBus? bus = null
    ) =>
        await SetRolePermissionsHandler.Handle(
            new SetRolePermissionsCommand(tenantId, roleId, Guid.NewGuid(), permissionIds),
            roles,
            users,
            new FakeTenantPlanLimitsStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus ?? new FakeMessageBus(),
            CancellationToken.None
        );

    /// <summary>
    /// §27 [D] — el bug análogo al de GitLab: el techo validaba el set COMPLETO al reguardar, así que
    /// un rol con un permiso dormido por un downgrade no se podía editar sin quitarlo primero. La
    /// configuración anterior tiene que quedar dormida, no borrada.
    /// </summary>
    [Fact]
    public async Task SetRolePermissionsHandler_lets_a_role_with_a_dormant_permission_be_edited()
    {
        var dormant = ProTierPermission();
        var staff = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [dormant, staff] };
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol con permiso dormido", null).Value;
        role.SetPermissions([dormant.Id]);
        roles.Seed(role);

        var result = await SetPermissionsAsync(
            roles,
            new FakeUserRepository(),
            tenantId,
            role.Id,
            [dormant.Id, staff.Id]
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, role.Permissions.Count);
    }

    [Fact]
    public async Task SetRolePermissionsHandler_still_rejects_a_permission_outside_the_plan_when_it_is_new()
    {
        var outsidePlan = ProTierPermission();
        var roles = new FakeRoleRepository { Catalog = [outsidePlan] };
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol sin permisos", null).Value;
        roles.Seed(role);

        var result = await SetPermissionsAsync(roles, new FakeUserRepository(), tenantId, role.Id, [outsidePlan.Id]);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.PermissionNotAssignable", result.Error.Code);
        Assert.Contains(outsidePlan.Code, result.Error.Message);
    }

    /// <summary>G8 — un rol creado para clientes del portal se mide contra CustomerPortal. Antes se
    /// medía siempre contra el staff, así que quedaba inmutable.</summary>
    [Fact]
    public async Task SetRolePermissionsHandler_lets_a_portal_role_keep_its_portal_permissions()
    {
        var portal = PortalPermission();
        var roles = new FakeRoleRepository { Catalog = [portal] };
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol de clientes", null, targetActorType: UserActorType.CustomerPortal).Value;
        roles.Seed(role);

        var result = await SetPermissionsAsync(roles, new FakeUserRepository(), tenantId, role.Id, [portal.Id]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task SetRolePermissionsHandler_rejects_a_staff_permission_in_a_portal_role()
    {
        var portal = PortalPermission();
        var staff = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [portal, staff] };
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol de clientes", null, targetActorType: UserActorType.CustomerPortal).Value;
        roles.Seed(role);

        var result = await SetPermissionsAsync(
            roles,
            new FakeUserRepository(),
            tenantId,
            role.Id,
            [portal.Id, staff.Id]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", result.Error.Code);
        Assert.Contains(staff.Code, result.Error.Message);
    }

    /// <summary>G7 — los titulares mandan: un rol sin destino declarado pero con titulares del portal
    /// no puede recibir un permiso de staff, y sí puede recibir uno de portal.</summary>
    [Fact]
    public async Task SetRolePermissionsHandler_validates_the_delta_against_the_actor_types_of_its_holders()
    {
        var portal = PortalPermission();
        var staff = StaffPermission();
        var roles = new FakeRoleRepository { Catalog = [portal, staff] };
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol legado de clientes", null).Value;
        roles.Seed(role);
        var users = new FakeUserRepository();
        users.Holders.Add(Holder(tenantId, UserActorType.CustomerPortal));

        var rejected = await SetPermissionsAsync(roles, users, tenantId, role.Id, [staff.Id]);
        Assert.True(rejected.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", rejected.Error.Code);

        var accepted = await SetPermissionsAsync(roles, users, tenantId, role.Id, [portal.Id]);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Message : null);
    }

    [Fact]
    public async Task UpdateRoleHandler_rejects_renaming_a_role_onto_another_existing_name()
    {
        var roles = new FakeRoleRepository();
        var tenantId = Guid.NewGuid();
        roles.Seed(Role.Create(tenantId, "Front desk", null).Value);
        var role = Role.Create(tenantId, "Marketing", null).Value;
        roles.Seed(role);

        var result = await UpdateRoleHandler.Handle(
            new UpdateRoleCommand(tenantId, role.Id, Guid.NewGuid(), "Front desk", null),
            roles,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NameConflict", result.Error.Code);
        Assert.Equal("Marketing", role.Name);
    }

    [Fact]
    public async Task UpdateRoleHandler_lets_a_role_keep_its_own_name()
    {
        var roles = new FakeRoleRepository();
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Marketing", null).Value;
        roles.Seed(role);

        var result = await UpdateRoleHandler.Handle(
            new UpdateRoleCommand(tenantId, role.Id, Guid.NewGuid(), "Marketing", "nueva descripción"),
            roles,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("nueva descripción", role.Description);
    }

    [Fact]
    public async Task ReactivateRoleHandler_puts_a_deactivated_role_back_in_service()
    {
        var roles = new FakeRoleRepository();
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol desactivado por error", null).Value;
        role.Deactivate();
        roles.Seed(role);

        var result = await ReactivateRoleHandler.Handle(
            new ReactivateRoleCommand(tenantId, role.Id, Guid.NewGuid()),
            roles,
            new FakeUserRepository(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(role.IsActive);
    }

    /// <summary>Reactivar devuelve el acceso a todos los titulares, así que tiene que avisarles.</summary>
    [Fact]
    public async Task ReactivateRoleHandler_publishes_the_fan_out_for_every_holder()
    {
        var roles = new FakeRoleRepository();
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol con titulares", null).Value;
        role.Deactivate();
        roles.Seed(role);
        var users = new FakeUserRepository();
        users.Holders.Add(Holder(tenantId, UserActorType.TenantEmployee));
        var bus = new FakeMessageBus();

        var result = await ReactivateRoleHandler.Handle(
            new ReactivateRoleCommand(tenantId, role.Id, Guid.NewGuid()),
            roles,
            users,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
    }

    [Fact]
    public async Task ReactivateRoleHandler_rejects_a_role_that_is_already_active()
    {
        var roles = new FakeRoleRepository();
        var tenantId = Guid.NewGuid();
        var role = Role.Create(tenantId, "Rol activo", null).Value;
        roles.Seed(role);

        var result = await ReactivateRoleHandler.Handle(
            new ReactivateRoleCommand(tenantId, role.Id, Guid.NewGuid()),
            roles,
            new FakeUserRepository(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.AlreadyActive", result.Error.Code);
    }
}
