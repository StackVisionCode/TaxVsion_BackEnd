using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Roles.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

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
}
