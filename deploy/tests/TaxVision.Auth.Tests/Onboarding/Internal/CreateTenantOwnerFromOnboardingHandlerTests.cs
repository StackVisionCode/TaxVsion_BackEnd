using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Internal.Commands;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding.Internal;

/// <summary>PayFlow (Fase 16) — CreateTenantOwnerFromOnboardingHandler: idempotencia por
/// OnboardingId, el password nunca cruza en claro (solo la referencia Redis ya hasheada), y el
/// TenantAdmin creado recibe su rol de sistema + bump de PermissionsVersion (mismo contrato que
/// AcceptInvitationHandler).</summary>
public sealed class CreateTenantOwnerFromOnboardingHandlerTests
{
    [Fact]
    public async Task Creates_the_owner_assigns_the_system_role_and_publishes_events()
    {
        var tenantId = Guid.NewGuid();
        var onboardingId = Guid.NewGuid();

        var permission = Permission.Seed(
            Guid.NewGuid(),
            "customers.view",
            "customers",
            "desc",
            isCustomerPortal: false
        );
        var systemRole = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        Assert.True(systemRole.SetPermissions([permission.Id], seeding: true).IsSuccess);

        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository { Catalog = [permission] };
        roles.Seed(systemRole);
        var tokenStore = new FakeTokenReferenceStore { ToConsume = "pbkdf2-hash-already-computed" };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var command = new CreateTenantOwnerFromOnboardingCommand(
            onboardingId,
            tenantId,
            "owner@acme.com",
            "Ada",
            "Lovelace",
            tokenStore.Reference
        );

        var result = await CreateTenantOwnerFromOnboardingHandler.Handle(
            command,
            users,
            roles,
            tokenStore,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(users.Added);
        Assert.Equal(onboardingId, users.Added!.OnboardingId);
        Assert.Equal("pbkdf2-hash-already-computed", users.Added.PasswordHash);
        Assert.True(users.Added.EmailVerified);
        Assert.Equal(1, users.Added.PermissionsVersion);
        Assert.NotNull(users.Added.PermissionsBackfilledAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var rolesChanged = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.Equal(users.Added.Id, rolesChanged.UserId);
        Assert.Contains(systemRole.Id, rolesChanged.RoleIds);
        Assert.Contains(permission.Code, rolesChanged.PermissionCodes);

        var ownerCreated = Assert.Single(bus.Published.OfType<TenantOwnerCreatedIntegrationEvent>());
        Assert.Equal(tenantId, ownerCreated.TenantId);
        Assert.Equal(onboardingId, ownerCreated.OnboardingId);
        Assert.Equal(users.Added.Id, ownerCreated.CreatedUserId);
    }

    [Fact]
    public async Task Is_idempotent_when_an_owner_already_exists_for_the_onboarding()
    {
        var tenantId = Guid.NewGuid();
        var onboardingId = Guid.NewGuid();
        var existing = User.Register(
            tenantId,
            "Ada",
            "Lovelace",
            "owner@acme.com",
            "hash",
            UserActorType.TenantAdmin,
            onboardingId: onboardingId
        ).Value;

        var users = new FakeUserRepository { Existing = existing };
        var roles = new FakeRoleRepository();
        var tokenStore = new FakeTokenReferenceStore { ToConsume = "should-not-be-consumed" };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await CreateTenantOwnerFromOnboardingHandler.Handle(
            new CreateTenantOwnerFromOnboardingCommand(
                onboardingId,
                tenantId,
                "owner@acme.com",
                "Ada",
                "Lovelace",
                Guid.NewGuid()
            ),
            users,
            roles,
            tokenStore,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(users.Added);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_without_creating_a_user_when_the_password_reference_has_expired()
    {
        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository();
        var tokenStore = new FakeTokenReferenceStore { ToConsume = null };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await CreateTenantOwnerFromOnboardingHandler.Handle(
            new CreateTenantOwnerFromOnboardingCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "owner@acme.com",
                "Ada",
                "Lovelace",
                Guid.NewGuid()
            ),
            users,
            roles,
            tokenStore,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.PasswordReferenceExpired", result.Error.Code);
        Assert.Null(users.Added);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? Existing { get; set; }
        public User? Added { get; private set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.OnboardingId == onboardingId ? Existing : null);

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
