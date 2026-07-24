using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// RBAC hardening follow-up — guardarraíl anti-auto-escalada en <see cref="AssignUserRolesHandler"/>:
/// nada impedía a un TenantAdmin asignarse roles a sí mismo (solo <c>RolePermissionGuard</c>/
/// <c>IsDangerous</c> lo frenaba indirectamente, sin una regla dura). Los fakes de este archivo
/// lanzan en cualquier método de acceso a datos, para probar que el guard corta ANTES de tocar
/// el repositorio — no solo que el resultado final es de error.
/// </summary>
public sealed class UserManagementCommandsTests
{
    private sealed class ThrowingUserRepository : IUserRepository
    {
        private static InvalidOperationException NotExpected() =>
            new("No debería consultarse — el guard anti-auto-escalada debe cortar antes de acceder a datos.");

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw NotExpected();

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            throw NotExpected();

        public Task AddAsync(User user, CancellationToken ct = default) => throw NotExpected();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) => throw NotExpected();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            CancellationToken ct = default
        ) => throw NotExpected();
    }

    private sealed class ThrowingRoleRepository : IRoleRepository
    {
        private static InvalidOperationException NotExpected() =>
            new("No debería consultarse — el guard anti-auto-escalada debe cortar antes de acceder a datos.");

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) => throw NotExpected();

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task AddAsync(Role role, CancellationToken ct = default) => throw NotExpected();

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) => throw NotExpected();

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            throw NotExpected();

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) => throw NotExpected();

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw NotExpected();
    }

    private sealed class ThrowingAuthAuditWriter : IAuthAuditWriter
    {
        public Task AddAsync(TaxVision.Auth.Domain.Audit.AuthAuditLog log, CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "No debería escribirse auditoría — el guard anti-auto-escalada debe cortar antes."
            );
    }

    private sealed class ThrowingRequestContext : IRequestContext
    {
        public string? IpAddress => throw new InvalidOperationException("No debería leerse.");
        public string? UserAgent => throw new InvalidOperationException("No debería leerse.");
    }

    private sealed class ThrowingCorrelationContext : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => throw new InvalidOperationException("No debería leerse.");

        public void Set(string correlationId) => throw new InvalidOperationException("No debería llamarse.");

        public IDisposable Push(string correlationId) => throw new InvalidOperationException("No debería llamarse.");
    }

    private sealed class ThrowingUnitOfWork : BuildingBlocks.Persistence.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("No debería guardarse — el guard debe cortar antes.");
    }

    [Fact]
    public async Task AssignUserRolesHandler_rejects_self_assignment_before_touching_any_repository()
    {
        var userId = Guid.NewGuid();
        var command = new AssignUserRolesCommand(
            TenantId: Guid.NewGuid(),
            TargetUserId: userId,
            RoleIds: [Guid.NewGuid()],
            AssignedByUserId: userId
        );

        var result = await AssignUserRolesHandler.Handle(
            command,
            new ThrowingUserRepository(),
            new ThrowingRoleRepository(),
            new ThrowingAuthAuditWriter(),
            new ThrowingRequestContext(),
            new ThrowingCorrelationContext(),
            new ThrowingUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.SelfAction", result.Error.Code);
    }
}
