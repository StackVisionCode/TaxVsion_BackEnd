using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Cubre el gap de producción encontrado el 2026-07-24: un usuario dado de alta por invitación
/// nunca bumpeaba <see cref="User.PermissionsVersion"/> ni publicaba
/// <see cref="UserRolesChangedIntegrationEvent"/>, así que quedaba sin fila de proyección de
/// permisos en ningún servicio para siempre — RBAC Fase 7/7.5 lo rechaza en frío. Ver
/// AcceptInvitation.cs para el fix real; este test prueba que el side-effect ocurre de verdad,
/// no solo que compila.
/// </summary>
public sealed class AcceptInvitationHandlerTests
{
    private const string RawToken = "raw-invitation-token";
    private const string FixedTokenHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class FakeInvitationRepository(Invitation invitation) : IInvitationRepository
    {
        public Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<Invitation?>(invitation);

        public Task<bool> HasPendingAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Invitation invitation, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<Invitation> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            InvitationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeInvitationTokenService : IInvitationTokenService
    {
        public InvitationToken Generate() => throw new NotSupportedException();

        public string Hash(string rawToken) => FixedTokenHash;
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? Added { get; private set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default)
        {
            Added = user;
            return Task.CompletedTask;
        }

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeTenantRegistry(Tenant tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Tenant?>(tenant);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string hash) => hash == $"hashed:{password}";
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly List<Role> _roles = [];
        public IReadOnlyList<Permission> Catalog { get; init; } = [];

        public void Seed(Role role) => _roles.Add(role);

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult(_roles.SingleOrDefault(r => r.Id == roleId));

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Role>>(_roles.Where(r => roleIds.Contains(r.Id)).ToList());

        public Task AddAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);

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
        ) => Task.CompletedTask;

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            Task.FromResult(_roles.SingleOrDefault(r => r.TenantId == tenantId && r.Name == systemRoleName));
    }

    private sealed class FakeAuthAuditWriter : IAuthAuditWriter
    {
        public Task AddAsync(AuthAuditLog log, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeRequestContext : IRequestContext
    {
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
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

    private static Permission StaffPermission() =>
        Permission.Seed(Guid.NewGuid(), "customers.view", "customers", "desc", isCustomerPortal: false);

    [Fact]
    public async Task AcceptInvitation_with_roles_bumps_version_and_publishes_UserRolesChanged()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Acme", "acme", TenantKind.Customer, "America/Santo_Domingo").Value;

        var permission = StaffPermission();
        var role = Role.Create(tenantId, "Staff", null).Value;
        Assert.True(role.SetPermissions([permission.Id]).IsSuccess);

        var invitation = Invitation
            .Create(
                tenantId,
                "newhire@example.com",
                UserActorType.TenantEmployee,
                customerId: null,
                invitedByUserId: Guid.NewGuid(),
                tokenHash: FixedTokenHash,
                expiresAtUtc: DateTime.UtcNow.AddDays(1),
                roleIdsJson: JsonSerializer.Serialize(new[] { role.Id })
            )
            .Value;

        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository { Catalog = [permission] };
        roles.Seed(role);
        var bus = new FakeMessageBus();

        var command = new AcceptInvitationCommand(
            InvitationToken: RawToken,
            Name: "Ana",
            LastName: "Gomez",
            Password: "Str0ng-Passw0rd!"
        );

        var result = await AcceptInvitationHandler.Handle(
            command,
            new FakeInvitationRepository(invitation),
            new FakeInvitationTokenService(),
            users,
            new FakeTenantRegistry(tenant),
            new FakePasswordHasher(),
            roles,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        // El bug real: sin el fix, esto quedaba en 0 para siempre y el usuario nunca aparecía
        // en ninguna UserPermissionsProjection — ProjectionPermissionsSource lo rechazaba en frío.
        Assert.NotNull(users.Added);
        Assert.Equal(1, users.Added!.PermissionsVersion);
        Assert.NotNull(users.Added.PermissionsBackfilledAt);

        var rolesChanged = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(users.Added.Id, rolesChanged.UserId);
        Assert.Equal(tenantId, rolesChanged.TenantId);
        Assert.Equal(1, rolesChanged.PermissionsVersion);
        Assert.Contains(role.Id, rolesChanged.RoleIds);
        Assert.Contains(permission.Code, rolesChanged.PermissionCodes);

        // El evento de alta ya existía antes del fix — confirma que no lo rompimos.
        Assert.Single(bus.Published.OfType<UserRegisteredIntegrationEvent>());
    }

    [Fact]
    public async Task AcceptInvitation_without_roles_does_not_publish_UserRolesChanged()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Acme", "acme", TenantKind.Customer, "America/Santo_Domingo").Value;

        // Sin RoleIdsJson y sin rol de sistema para PlatformAdmin: ResolveInvitationRoleIds
        // devuelve vacío, y el switch de rol de sistema no cubre PlatformAdmin.
        var invitation = Invitation
            .Create(
                Tenant.Register(PlatformTenant.Id, "Platform", "platform", TenantKind.Platform, "UTC").Value.Id,
                "admin@example.com",
                UserActorType.PlatformAdmin,
                customerId: null,
                invitedByUserId: Guid.NewGuid(),
                tokenHash: FixedTokenHash,
                expiresAtUtc: DateTime.UtcNow.AddDays(1)
            )
            .Value;

        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository();
        var bus = new FakeMessageBus();

        var command = new AcceptInvitationCommand(
            InvitationToken: RawToken,
            Name: "Platform",
            LastName: "Owner",
            Password: "Str0ng-Passw0rd!"
        );

        var result = await AcceptInvitationHandler.Handle(
            command,
            new FakeInvitationRepository(invitation),
            new FakeInvitationTokenService(),
            users,
            new FakeTenantRegistry(
                Tenant.Register(PlatformTenant.Id, "Platform", "platform", TenantKind.Platform, "UTC").Value
            ),
            new FakePasswordHasher(),
            roles,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(users.Added);
        Assert.Equal(0, users.Added!.PermissionsVersion);
        Assert.Null(users.Added.PermissionsBackfilledAt);
        Assert.Empty(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
    }

    /// <summary>
    /// El camino más común en producción: una invitación SIN RoleIdsJson explícito, donde
    /// ResolveInvitationRoleIds cae al switch de rol de sistema por ActorType
    /// (GetSystemRoleAsync) — código distinto al de RoleIdsJson explícito, y hasta ahora sin
    /// cobertura para TenantEmployee/CustomerPortal.
    /// </summary>
    [Theory]
    [InlineData(UserActorType.TenantEmployee, Role.SystemEmployee)]
    [InlineData(UserActorType.CustomerPortal, Role.SystemCustomerPortal)]
    public async Task AcceptInvitation_falls_back_to_system_role_and_still_bumps_version(
        UserActorType actorType,
        string systemRoleName
    )
    {
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Register(tenantId, "Acme", "acme", TenantKind.Customer, "America/Santo_Domingo").Value;
        var customerId = actorType == UserActorType.CustomerPortal ? Guid.NewGuid() : (Guid?)null;

        var permission = StaffPermission();
        var systemRole = Role.Create(tenantId, systemRoleName, null, isSystem: true).Value;
        // seeding: true — los roles de sistema solo aceptan SetPermissions vía el seeder
        // (SystemRolePermissionsSyncService), no por edición normal (Role.System si no).
        Assert.True(systemRole.SetPermissions([permission.Id], seeding: true).IsSuccess);

        // Sin RoleIdsJson: obliga a pasar por GetSystemRoleAsync, no por el camino ya probado.
        var invitation = Invitation
            .Create(
                tenantId,
                "newhire@example.com",
                actorType,
                customerId: customerId,
                invitedByUserId: Guid.NewGuid(),
                tokenHash: FixedTokenHash,
                expiresAtUtc: DateTime.UtcNow.AddDays(1)
            )
            .Value;

        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository { Catalog = [permission] };
        roles.Seed(systemRole);
        var bus = new FakeMessageBus();

        var command = new AcceptInvitationCommand(
            InvitationToken: RawToken,
            Name: "Ana",
            LastName: "Gomez",
            Password: "Str0ng-Passw0rd!"
        );

        var result = await AcceptInvitationHandler.Handle(
            command,
            new FakeInvitationRepository(invitation),
            new FakeInvitationTokenService(),
            users,
            new FakeTenantRegistry(tenant),
            new FakePasswordHasher(),
            roles,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(users.Added);
        Assert.Equal(1, users.Added!.PermissionsVersion);
        Assert.NotNull(users.Added.PermissionsBackfilledAt);

        var rolesChanged = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(users.Added.Id, rolesChanged.UserId);
        Assert.Contains(systemRole.Id, rolesChanged.RoleIds);
        Assert.Contains(permission.Code, rolesChanged.PermissionCodes);
    }
}
