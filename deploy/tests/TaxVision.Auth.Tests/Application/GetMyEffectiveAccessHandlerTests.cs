using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>GET /auth/me/effective-access (Fase 7): por permiso reporta su módulo y si es efectivo
/// (transversal, o módulo habilitado en el plan). Un permiso concedido pero con su módulo deshabilitado
/// sale Effective=false — el "por qué 403".</summary>
public sealed class GetMyEffectiveAccessHandlerTests
{
    [Fact]
    public async Task Marks_permissions_effective_by_module_and_enabled_plan_modules()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Register(tenantId, "Ada", "Lovelace", "ada@acme.com", "hash", UserActorType.TenantAdmin).Value;

        var users = new FakeUserRepository { Seeded = user };
        var roles = new FakeRoleRepository
        {
            // signatures gateado (habilitado), customers gateado (NO habilitado), billing transversal.
            EffectiveCodes = ["signature.request.read", "customers.view", "billing.view"],
        };
        var limits = new FakePlanLimitsStore
        {
            Seeded = TenantPlanLimits.Create(tenantId, "pro", 10, 10, 0, "[\"signatures\"]"),
        };

        var result = await GetMyEffectiveAccessHandler.Handle(
            new GetMyEffectiveAccessQuery(user.Id),
            users,
            roles,
            limits,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("TenantAdmin", result.Value.ActorType);
        Assert.Equal(["signatures"], result.Value.EnabledModules);

        var signature = result.Value.Permissions.Single(p => p.Code == "signature.request.read");
        Assert.Equal("signatures", signature.Module);
        Assert.True(signature.Effective); // módulo habilitado

        var customers = result.Value.Permissions.Single(p => p.Code == "customers.view");
        Assert.Equal("customers", customers.Module);
        Assert.False(customers.Effective); // módulo NO habilitado → el "por qué 403"

        var billing = result.Value.Permissions.Single(p => p.Code == "billing.view");
        Assert.Null(billing.Module); // transversal
        Assert.True(billing.Effective); // sin módulo = siempre efectivo
    }

    [Fact]
    public async Task Fails_when_the_user_does_not_exist()
    {
        var result = await GetMyEffectiveAccessHandler.Handle(
            new GetMyEffectiveAccessQuery(Guid.NewGuid()),
            new FakeUserRepository { Seeded = null },
            new FakeRoleRepository(),
            new FakePlanLimitsStore(),
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
        public IReadOnlyList<string> EffectiveCodes { get; init; } = [];

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

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

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            EnsureSystemRolesAsync(tenantId, ct);

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePlanLimitsStore : ITenantPlanLimitsStore
    {
        public TenantPlanLimits? Seeded { get; set; }

        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Seeded);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
