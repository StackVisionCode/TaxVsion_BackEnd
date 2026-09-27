using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
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
    private class ThrowingUserRepository : IUserRepository
    {
        private static InvalidOperationException NotExpected() =>
            new("No debería consultarse — el guard debe cortar antes de acceder a datos.");

        public virtual Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw NotExpected();

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task AddAsync(User user, CancellationToken ct = default) => throw NotExpected();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) => throw NotExpected();

        public virtual Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) => throw NotExpected();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
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
            throw NotExpected();
    }

    private sealed class ThrowingSessionRepository : ISessionRepository
    {
        private static InvalidOperationException NotExpected() =>
            new("No debería tocarse la sesión — el guard debe cortar antes.");

        public Task AddSessionAsync(UserSession session, CancellationToken ct = default) => throw NotExpected();

        public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) => throw NotExpected();

        public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<int> RevokeSurfaceTokensAsync(
            Guid sessionId,
            SessionSurface surface,
            string reason,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken ct = default
        ) => throw NotExpected();

        public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default) =>
            throw NotExpected();
    }

    /// <summary>A5 — el anuncio de revocación no debería salir si el guard corta antes.</summary>
    private sealed class ThrowingSessionRevocationPublisher : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("No debería anunciarse ninguna revocación — el guard corta antes.");
    }

    private sealed class ThrowingDenylist : IAccessTokenDenylist
    {
        private static InvalidOperationException NotExpected() =>
            new("No debería denylistarse nada — el guard debe cortar antes.");

        public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default) =>
            throw NotExpected();

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) => throw NotExpected();
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

    /// <summary>
    /// Retirar de la oficina es para quien TRABAJA en ella: reasigna su trabajo a un sucesor, transfiere sus
    /// archivos compartidos, suelta sus conectores y libera su asiento. Un cliente de portal no tiene nada de
    /// eso —ni siquiera asiento—, y su acceso se quita desde su propio perfil. El corte va acá y no solo en
    /// la pantalla: que todo lo demás sea un doble que lanza prueba que nada se tocó.
    /// </summary>
    [Fact]
    public async Task OffboardUserHandler_refuses_to_remove_a_portal_client()
    {
        var tenantId = Guid.NewGuid();
        var client = User.Register(
            tenantId,
            "Ada",
            "Lovelace",
            "ada@cliente.test",
            "hash",
            UserActorType.CustomerPortal,
            customerId: Guid.NewGuid()
        ).Value;

        var result = await OffboardUserHandler.Handle(
            new OffboardUserCommand(tenantId, client.Id, Guid.NewGuid(), SuccessorUserId: null),
            new SingleUserRepository(client),
            new ThrowingSessionRepository(),
            new ThrowingDenylist(),
            new ThrowingSessionRevocationPublisher(),
            new ThrowingAuthAuditWriter(),
            new ThrowingRequestContext(),
            new ThrowingCorrelationContext(),
            new ThrowingUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.PortalClient", result.Error.Code);
    }

    /// <summary>Solo resuelve el usuario objetivo; cualquier otra consulta sigue siendo un fallo del test.</summary>
    private sealed class SingleUserRepository(User user) : ThrowingUserRepository
    {
        public override Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<User?>(user);
    }

    /// <summary>Como la anterior, pero además dice cuántos administradores activos hay.</summary>
    private sealed class SingleUserWithAdminCountRepository(User user, int activeAdmins) : ThrowingUserRepository
    {
        public override Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<User?>(user);

        public override Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(activeAdmins);
    }

    private static User Admin(Guid tenantId) =>
        User.Register(tenantId, "Tania", "Ruiz", "tania@example.com", "hash", UserActorType.TenantAdmin).Value;

    private static User Employee(Guid tenantId) =>
        User.Register(tenantId, "Elio", "Paz", "elio@example.com", "hash", UserActorType.TenantEmployee).Value;

    private static Task<BuildingBlocks.Results.Result> DeactivateAsync(
        Guid tenantId,
        User target,
        UserActorType? callerActorType,
        IUserRepository users
    ) =>
        DeactivateUserHandler.Handle(
            new DeactivateUserCommand(tenantId, target.Id, Guid.NewGuid(), callerActorType),
            users,
            new ThrowingSessionRepository(),
            new ThrowingDenylist(),
            new ThrowingSessionRevocationPublisher(),
            new ThrowingAuthAuditWriter(),
            new ThrowingRequestContext(),
            new ThrowingCorrelationContext(),
            new ThrowingUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

    /// <summary>
    /// <c>users.manage</c> alcanza para dar de baja a un empleado, no a un administrador: un empleado
    /// con el permiso delegado podía desactivar a su propio jefe.
    /// </summary>
    [Theory]
    [InlineData(UserActorType.TenantEmployee)]
    [InlineData(UserActorType.CustomerPortal)]
    [InlineData(null)]
    public async Task DeactivateUserHandler_refuses_a_non_admin_caller_against_an_admin_target(
        UserActorType? callerActorType
    )
    {
        var tenantId = Guid.NewGuid();
        var target = Admin(tenantId);

        var result = await DeactivateAsync(tenantId, target, callerActorType, new SingleUserRepository(target));

        Assert.True(result.IsFailure);
        Assert.Equal("User.Hierarchy", result.Error.Code);
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task DeactivateUserHandler_refuses_to_leave_the_tenant_without_an_active_admin()
    {
        var tenantId = Guid.NewGuid();
        var target = Admin(tenantId);

        var result = await DeactivateAsync(
            tenantId,
            target,
            UserActorType.TenantAdmin,
            new SingleUserWithAdminCountRepository(target, activeAdmins: 1)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.LastAdmin", result.Error.Code);
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task DeactivateUserHandler_lets_an_employee_caller_deactivate_another_employee()
    {
        // Regresión: la jerarquía nueva no toca el caso normal (dar de baja a un empleado con
        // users.manage sigue funcionando para un empleado).
        var tenantId = Guid.NewGuid();
        var target = Employee(tenantId);

        var result = await DeactivateUserHandler.Handle(
            new DeactivateUserCommand(tenantId, target.Id, Guid.NewGuid(), UserActorType.TenantEmployee),
            new SingleUserRepository(target),
            new QuietSessionRepository(),
            new QuietDenylist(),
            new RecordingSessionRevocationPublisher(),
            new QuietAuthAuditWriter(),
            new QuietRequestContext(),
            new QuietCorrelationContext(),
            new QuietUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.False(target.IsActive);
    }

    /// <summary>Dobles silenciosos para el camino feliz: acá el test mira el resultado, no los efectos.</summary>
    private sealed class QuietSessionRepository : ISessionRepository
    {
        public Task AddSessionAsync(UserSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult<UserSession?>(null);

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserSession>>([]);

        public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<RefreshToken?>(null);

        public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<int> RevokeSurfaceTokensAsync(
            Guid sessionId,
            SessionSurface surface,
            string reason,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    /// <summary>A5 — registra los anuncios de revocación para poder afirmar que salieron.</summary>
    private sealed class RecordingSessionRevocationPublisher : ISessionRevocationPublisher
    {
        public List<(Guid TenantId, Guid UserId, Guid SessionId, string Reason)> Published { get; } = [];

        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            Published.Add((tenantId, userId, sessionId, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class QuietDenylist : IAccessTokenDenylist
    {
        public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class QuietAuthAuditWriter : IAuthAuditWriter
    {
        public Task AddAsync(TaxVision.Auth.Domain.Audit.AuthAuditLog log, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class QuietUnitOfWork : BuildingBlocks.Persistence.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class QuietRequestContext : IRequestContext
    {
        public string? IpAddress => "203.0.113.10";
        public string? UserAgent => "test-agent";
    }

    private sealed class QuietCorrelationContext : BuildingBlocks.Common.ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
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
