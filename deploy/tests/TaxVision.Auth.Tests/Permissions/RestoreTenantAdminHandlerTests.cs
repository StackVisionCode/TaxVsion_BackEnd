using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Permissions.Admin.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;
using TaxVision.Auth.Tests.Domain;

namespace TaxVision.Auth.Tests.Permissions;

/// <summary>
/// Break-glass de PlatformAdmin: una cuenta que quedó PlatformAdmin dentro de una oficina vuelve a
/// TenantAdmin y cada servicio se entera por UserRolesChanged con el nuevo tipo de actor.
/// </summary>
public sealed class RestoreTenantAdminHandlerTests
{
    private static readonly Guid PlatformAdminId = Guid.NewGuid();

    private readonly InMemoryUserRepository _users = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly RecordingAccessTokenDenylist _denylist = new();
    private readonly RecordingRevocations _revocations = new();
    private readonly FakeAuthAuditWriter _audit = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();
    private readonly RecordingTenantContext _tenantContext = new();

    private Task<BuildingBlocks.Results.Result<RestoreTenantAdminResult>> HandleAsync(Guid userId, FakeRoles roles) =>
        RestoreTenantAdminHandler.Handle(
            new RestoreTenantAdminCommand(userId, PlatformAdminId),
            _users,
            roles,
            _tenantContext,
            _sessions,
            _denylist,
            _revocations,
            _audit,
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            _unitOfWork,
            _bus,
            NullLogger<RestoreTenantAdminCommand>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Restores_the_owner_and_tells_every_service_it_is_a_tenant_admin()
    {
        var user = UserRestoreAsTenantAdminTests.MisplacedPlatformAdmin(onboardingId: Guid.NewGuid());
        var versionBefore = user.PermissionsVersion;
        _users.Add(user);
        var session = UserSession.Start(user.TenantId, user.Id, "Chrome en Windows", "agent", "203.0.113.10");
        await _sessions.AddSessionAsync(session);
        var roles = FakeRoles.WithTenantAdminRole(user.TenantId, "customers.view", "documents.manage");

        var result = await HandleAsync(user.Id, roles);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(result.Value.IsOwner);
        Assert.Equal(2, result.Value.PermissionCodeCount);
        Assert.Equal(1, result.Value.RevokedSessions);

        // Roles de la oficina del usuario, no de quien llama.
        Assert.Equal(user.TenantId, _tenantContext.SetTo);
        Assert.Equal([roles.SystemRole.Id], roles.Replaced);

        var evt = Assert.Single(_bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal("TenantAdmin", evt.ActorType);
        Assert.Equal(user.Id, evt.UserId);
        Assert.Equal(versionBefore + 1, evt.PermissionsVersion);
        Assert.Equal([roles.SystemRole.Id], evt.RoleIds);
        Assert.Equal(["customers.view", "documents.manage"], evt.PermissionCodes.Order());

        // El JWT vigente dice PlatformAdmin: se corta para que el próximo login traiga el tipo correcto.
        Assert.Equal([session.Id], _denylist.Denied);
        Assert.Single(_revocations.Published);

        var log = Assert.Single(_audit.Logs);
        Assert.Equal(AuthAuditAction.UserRestoredAsTenantAdmin, log.Action);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_real_platform_admin_is_rejected_without_side_effects()
    {
        var user = User.Register(
            PlatformTenant.Id,
            "Jorge",
            "Turbi",
            "ops@platform.test",
            "hash",
            UserActorType.PlatformAdmin
        ).Value;
        _users.Add(user);
        var roles = FakeRoles.WithTenantAdminRole(PlatformTenant.Id);

        var result = await HandleAsync(user.Id, roles);

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotMisplacedPlatformAdmin", result.Error.Code);
        Assert.Empty(_bus.Published);
        Assert.Null(roles.Replaced);
        Assert.Empty(_denylist.Denied);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var result = await HandleAsync(Guid.NewGuid(), FakeRoles.WithTenantAdminRole(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotFound", result.Error.Code);
        Assert.Empty(_bus.Published);
    }

    private sealed class RecordingTenantContext : ITenantContext
    {
        public Guid? SetTo { get; private set; }
        public Guid TenantId => SetTo ?? throw new InvalidOperationException("TenantId is not set");
        public bool HasTenant => SetTo.HasValue;

        public void SetTenant(Guid tenantId) => SetTo = tenantId;
    }

    private sealed class RecordingRevocations : ISessionRevocationPublisher
    {
        public List<Guid> Published { get; } = [];

        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            Published.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Solo lo que usa el handler: el rol de sistema Tenant Admin con sus permisos.</summary>
    private sealed class FakeRoles : IRoleRepository
    {
        private readonly List<Permission> _catalog = [];

        private FakeRoles(Role systemRole) => SystemRole = systemRole;

        public Role SystemRole { get; }
        public IReadOnlyCollection<Guid>? Replaced { get; private set; }

        public static FakeRoles WithTenantAdminRole(Guid tenantId, params string[] codes)
        {
            var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
            var fake = new FakeRoles(role);
            foreach (var code in codes)
                fake._catalog.Add(Permission.Seed(Guid.NewGuid(), code, code.Split('.')[0], "desc"));
            role.SetPermissions(fake._catalog.Select(permission => permission.Id).ToList(), seeding: true);
            return fake;
        }

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            Task.FromResult<Role?>(SystemRole);

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        )
        {
            Replaced = roleIds;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Role>>([SystemRole]);

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Permission>>(_catalog);

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<PermissionDenyInput> denies,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
