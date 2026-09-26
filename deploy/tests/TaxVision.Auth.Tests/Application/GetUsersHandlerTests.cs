using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>GET /auth/users devuelve en `Roles` solo los roles asignados reales (Role.Name), no el
/// actor_type-como-pseudo-rol (user.Roles): antes salían duplicados "TenantAdmin" + "Tenant Admin".</summary>
public sealed class GetUsersHandlerTests
{
    [Fact]
    public async Task Roles_contain_the_assigned_role_name_but_not_the_actor_type_pseudo_role()
    {
        var tenantId = Guid.NewGuid();
        // Register siembra user.Roles con el actor_type ("TenantAdmin").
        var user = User.Register(
            tenantId,
            "Carlos",
            "Castillo",
            "carlos@acme.com",
            "hash",
            UserActorType.TenantAdmin
        ).Value;
        var systemRole = Role.Create(tenantId, "Tenant Admin", null, isSystem: true).Value;

        var users = new FakeUserRepository { Page = [user] };
        var roles = new FakeRoleRepository { UserRoles = [systemRole] };

        var result = await GetUsersHandler.Handle(new GetUsersQuery(tenantId), users, roles, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal("TenantAdmin", row.ActorType); // el actor_type viaja en su propio campo
        Assert.Contains("Tenant Admin", row.Roles); // rol de sistema real
        Assert.DoesNotContain("TenantAdmin", row.Roles); // pseudo-rol ya NO se duplica en Roles
        Assert.Equal("Active", row.Status); // ciclo de vida por defecto
    }

    // Un usuario retirado debe distinguirse de uno solo suspendido (ambos IsActive=false).
    [Fact]
    public async Task Status_reflects_offboarded_so_the_ui_can_distinguish_it_from_suspended()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Register(
            tenantId,
            "Sofia",
            "Martinez",
            "sofia@acme.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        user.Offboard(DateTime.UtcNow);

        var users = new FakeUserRepository { Page = [user] };
        var roles = new FakeRoleRepository();

        var result = await GetUsersHandler.Handle(new GetUsersQuery(tenantId), users, roles, CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal("Offboarded", row.Status);
        Assert.False(row.IsActive);
    }

    /// <summary>
    /// "Team members" y el acceso al portal del perfil del cliente leen la MISMA lista. Sin decir de qué tipo
    /// de cuenta se habla, los clientes salían entre el personal y se les ofrecían acciones de empleado.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(UserAccountKind.Staff)]
    [InlineData(UserAccountKind.Portal)]
    public async Task The_list_asks_the_repository_for_the_kind_of_account_it_wants(UserAccountKind? kind)
    {
        var users = new FakeUserRepository();

        await GetUsersHandler.Handle(
            new GetUsersQuery(Guid.NewGuid(), 1, 20, null, null, null, kind),
            users,
            new FakeRoleRepository(),
            CancellationToken.None
        );

        Assert.Equal(kind, users.AskedForKind);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public IReadOnlyList<User> Page { get; init; } = [];

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        )
        {
            AskedForKind = accountKind;
            return Task.FromResult((Page, Page.Count));
        }

        /// <summary>Qué tipo de cuenta pidió el handler — lo comprueba el test del filtro.</summary>
        public UserAccountKind? AskedForKind { get; private set; }

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
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        public IReadOnlyList<Role> UserRoles { get; init; } = [];

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(UserRoles);

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
            EnsureSystemRolesAsync(tenantId, ct);

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> permissionIds,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
