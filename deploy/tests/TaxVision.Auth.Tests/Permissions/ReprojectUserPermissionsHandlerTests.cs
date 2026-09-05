using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Permissions.Admin.Commands;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Permissions;

/// <summary>Break-glass de PlatformAdmin (A4): ReprojectUserPermissionsHandler re-publica
/// UserRolesChanged con el set ACTUAL del usuario para re-materializar su proyección local en cada
/// servicio, sin reiniciar Auth. Sella el tenant del usuario objetivo antes de resolver roles/códigos
/// (cross-tenant) y NO bumpea la versión (no invalida JWT vigentes).</summary>
public sealed class ReprojectUserPermissionsHandlerTests
{
    [Fact]
    public async Task Republishes_user_roles_changed_at_the_current_version_with_the_effective_set()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Register(
            tenantId,
            "Ada",
            "Lovelace",
            "owner@acme.com",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        user.BumpPermissionsVersion(); // versión vigente = 1
        var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;

        var users = new FakeUserRepository { Seeded = user };
        var roles = new FakeRoleRepository
        {
            UserRoles = [role],
            EffectiveCodes = ["customers.view", "documents.manage"],
        };
        var tenantContext = new FakeTenantContext();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReprojectUserPermissionsHandler.Handle(
            new ReprojectUserPermissionsCommand(user.Id),
            users,
            roles,
            tenantContext,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<ReprojectUserPermissionsCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal(1, result.Value.PermissionsVersion);
        Assert.Equal(2, result.Value.PermissionCodeCount);
        Assert.Equal(1, result.Value.RoleCount);

        // El tenant del usuario objetivo se sella antes de resolver sus roles/códigos (cross-tenant).
        Assert.Equal(tenantId, tenantContext.SetTo);

        var evt = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(user.Id, evt.UserId);
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal(1, evt.PermissionsVersion); // re-proyecta a la versión vigente, NO la bumpea
        Assert.Contains(role.Id, evt.RoleIds);
        Assert.Contains("customers.view", evt.PermissionCodes);
        Assert.Contains("documents.manage", evt.PermissionCodes);
        Assert.Equal(UserActorType.TenantAdmin.ToString(), evt.ActorType);

        Assert.NotNull(user.PermissionsBackfilledAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, user.PermissionsVersion); // sin bump
    }

    [Fact]
    public async Task Fails_when_the_user_does_not_exist()
    {
        var users = new FakeUserRepository { Seeded = null };
        var roles = new FakeRoleRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReprojectUserPermissionsHandler.Handle(
            new ReprojectUserPermissionsCommand(Guid.NewGuid()),
            users,
            roles,
            new FakeTenantContext(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<ReprojectUserPermissionsCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotFound", result.Error.Code);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Republishes_an_empty_set_when_the_user_has_no_roles_so_the_count_signals_the_real_problem()
    {
        // Si el usuario no tiene rol asignado (el modo de fallo distinto: asignación, no proyección),
        // re-proyectar un set vacío no es dañino y PermissionCodeCount=0 es la señal para el operador.
        var tenantId = Guid.NewGuid();
        var user = User.Register(
            tenantId,
            "Grace",
            "Hopper",
            "grace@acme.com",
            "hash",
            UserActorType.TenantAdmin
        ).Value;

        var users = new FakeUserRepository { Seeded = user };
        var roles = new FakeRoleRepository { UserRoles = [], EffectiveCodes = [] };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReprojectUserPermissionsHandler.Handle(
            new ReprojectUserPermissionsCommand(user.Id),
            users,
            roles,
            new FakeTenantContext(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<ReprojectUserPermissionsCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(0, result.Value.PermissionCodeCount);
        Assert.Equal(0, result.Value.RoleCount);
        var evt = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Empty(evt.PermissionCodes);
        Assert.Empty(evt.RoleIds);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? Seeded { get; set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Seeded is not null && Seeded.Id == id ? Seeded : null);

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeRoleRepository : IRoleRepository
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

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? SetTo { get; private set; }
        public Guid TenantId => SetTo ?? throw new InvalidOperationException("TenantId is not set");
        public bool HasTenant => SetTo.HasValue;

        public void SetTenant(Guid tenantId) => SetTo = tenantId;
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
