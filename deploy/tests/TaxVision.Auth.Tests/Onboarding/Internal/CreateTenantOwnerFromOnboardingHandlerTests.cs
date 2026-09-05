using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Internal.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding.Internal;

/// <summary>PayFlow (Fase 16) — CreateTenantOwnerFromOnboardingHandler: idempotencia por
/// OnboardingId, el password nunca cruza en claro (solo la referencia Redis ya hasheada), y el
/// TenantAdmin creado recibe su rol de sistema + bump de PermissionsVersion (mismo contrato que
/// AcceptInvitationHandler).
/// <para>
/// Gap real cerrado en esta sesión: <c>AcceptTermsFromOnboardingCommand</c> nunca tenía un caller
/// real en todo el codebase (ver su doc-comment, "UoW #8 de la Saga") — la aceptación de términos
/// del onboarding quedaba solo en los campos planos de <c>TenantOnboarding</c>, nunca en el ledger
/// de auditoría dedicado <c>TenantTermsAcceptances</c>. Este handler es el primer punto donde
/// Tenant y User ya existen simultáneamente, así que es donde se cierra ese registro.
/// </para></summary>
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
        var onboardings = new FakeTenantOnboardingRepository();
        var termsAcceptances = new FakeTenantTermsAcceptanceRepository();
        var termsVersions = new FakeTermsVersionRepository();
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
            onboardings,
            termsAcceptances,
            termsVersions,
            new FakeAuthAuditWriter(),
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

        // Sin onboarding en el repo (no seedeado), el bloque de aceptación de términos no debe
        // intentar escribir nada — no hay TermsVersionId/TermsContentHash que leer.
        Assert.Null(termsAcceptances.Added);
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
            new FakeTenantOnboardingRepository(),
            new FakeTenantTermsAcceptanceRepository(),
            new FakeTermsVersionRepository(),
            new FakeAuthAuditWriter(),
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
            new FakeTenantOnboardingRepository(),
            new FakeTenantTermsAcceptanceRepository(),
            new FakeTermsVersionRepository(),
            new FakeAuthAuditWriter(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.PasswordReferenceExpired", result.Error.Code);
        Assert.Null(users.Added);
        Assert.Empty(bus.Published);
        // El ensure idempotente de roles corre ANTES de consumir el password (para no gastar el
        // one-shot si el seed fallara) → 1 save de roles; el owner no se crea ni se publican eventos.
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Seeds_the_system_role_inline_when_it_was_not_yet_present_so_the_owner_never_lands_without_permissions()
    {
        // Regresión del bug "403 en TODO" de signups nuevos: en el onboarding pago-primero este handler
        // puede correr ANTES de que TenantCreatedConsumer siembre los roles de sistema. Antes, systemRole
        // quedaba null y el bloque de asignación se salteaba en silencio → owner sin rol, perm_v=0, sin
        // UserRolesChanged → proyección vacía en todos los servicios. Ahora siembra inline y asigna igual.
        var tenantId = Guid.NewGuid();
        var onboardingId = Guid.NewGuid();

        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository { Catalog = [] }; // NINGÚN rol de sistema pre-sembrado
        var tokenStore = new FakeTokenReferenceStore { ToConsume = "pbkdf2-hash-already-computed" };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await CreateTenantOwnerFromOnboardingHandler.Handle(
            new CreateTenantOwnerFromOnboardingCommand(
                onboardingId,
                tenantId,
                "owner@acme.com",
                "Ada",
                "Lovelace",
                tokenStore.Reference
            ),
            users,
            roles,
            tokenStore,
            new FakeTenantOnboardingRepository(),
            new FakeTenantTermsAcceptanceRepository(),
            new FakeTermsVersionRepository(),
            new FakeAuthAuditWriter(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(users.Added);
        Assert.Equal(1, users.Added!.PermissionsVersion); // se asignó el rol (bump), no quedó en 0
        Assert.NotNull(users.Added.PermissionsBackfilledAt);
        var rolesChanged = Assert.Single(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
        Assert.NotEmpty(rolesChanged.RoleIds); // el owner NUNCA nace sin rol
    }

    [Fact]
    public async Task Records_the_terms_acceptance_ledger_when_the_onboarding_captured_terms_data()
    {
        var tenantId = Guid.NewGuid();
        var onboardingId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var termsVersions = new FakeTermsVersionRepository();
        var termsVersion = termsVersions.Seed(TermsKind.TermsOfService, "2026.1", "en-US", now.AddDays(-1));

        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        Assert.True(onboarding.MarkPaymentProcessing(Guid.NewGuid(), "pi_123").IsSuccess);
        Assert.True(onboarding.MarkPaymentCompleted("pi_123", now).IsSuccess);
        Assert.True(
            onboarding
                .SetRegistrationToken(RegistrationTokenHash.Create(new string('a', 64)).Value, now.AddHours(72))
                .IsSuccess
        );
        Assert.True(
            onboarding
                .StartProvisioning(
                    "Ada's Tax Office",
                    "adas-office",
                    termsVersion.Id,
                    termsVersion.ContentHash!,
                    "203.0.113.7",
                    "xunit-agent",
                    now
                )
                .IsSuccess
        );

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
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var termsAcceptances = new FakeTenantTermsAcceptanceRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await CreateTenantOwnerFromOnboardingHandler.Handle(
            new CreateTenantOwnerFromOnboardingCommand(
                onboardingId,
                tenantId,
                "owner@acme.com",
                "Ada",
                "Lovelace",
                tokenStore.Reference
            ),
            users,
            roles,
            tokenStore,
            onboardings,
            termsAcceptances,
            termsVersions,
            new FakeAuthAuditWriter(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(termsAcceptances.Added);
        Assert.Equal(tenantId, termsAcceptances.Added!.TenantId);
        Assert.Equal(users.Added!.Id, termsAcceptances.Added.AcceptedByUserId);
        Assert.Equal(termsVersion.Id, termsAcceptances.Added.TermsVersionId);
        Assert.Equal("203.0.113.7", termsAcceptances.Added.AcceptedFromIp);
        Assert.Equal("xunit-agent", termsAcceptances.Added.UserAgent);
        Assert.Equal("Onboarding", termsAcceptances.Added.AcceptedInContext);
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
            Guid? customerId = null,
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

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default)
        {
            // Mimic del repo real (idempotente): siembra el rol de sistema TenantAdmin si aún no está,
            // para que GetSystemRoleAsync lo encuentre después. Cubre la rama de carrera del handler.
            if (!_roles.Any(r => r.TenantId == tenantId && r.Name == Role.SystemTenantAdmin))
                _roles.Add(Role.Create(tenantId, Role.SystemTenantAdmin, "System role", isSystem: true).Value);
            return Task.CompletedTask;
        }

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            Task.FromResult(_roles.SingleOrDefault(r => r.TenantId == tenantId && r.Name == systemRoleName));
    }

    private sealed class FakeTenantTermsAcceptanceRepository : ITenantTermsAcceptanceRepository
    {
        private readonly List<TenantTermsAcceptance> _all = [];

        public TenantTermsAcceptance? Added { get; private set; }

        public Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default)
        {
            Added = acceptance;
            _all.Add(acceptance);
            return Task.CompletedTask;
        }

        public Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(
                _all.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.AcceptedAtUtc).FirstOrDefault()
            );

        public Task<TenantTermsAcceptance?> GetByVersionAsync(
            Guid tenantId,
            Guid userId,
            Guid termsVersionId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.FirstOrDefault(a =>
                    a.TenantId == tenantId && a.AcceptedByUserId == userId && a.TermsVersionId == termsVersionId
                )
            );
    }

    private sealed class FakeTermsVersionRepository : ITermsVersionRepository
    {
        private readonly List<TermsVersion> _all = [];

        public TermsVersion Seed(TermsKind kind, string version, string locale, DateTime effectiveFromUtc)
        {
            var published = TermsVersion
                .Publish(kind, version, Guid.NewGuid(), new string('a', 64), locale, Guid.NewGuid(), effectiveFromUtc)
                .Value;
            _all.Add(published);
            return published;
        }

        public Task AddAsync(TermsVersion version, CancellationToken ct = default)
        {
            _all.Add(version);
            return Task.CompletedTask;
        }

        public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_all.FirstOrDefault(v => v.Id == id));

        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.Where(v =>
                        v.Kind == kind
                        && v.Locale == locale
                        && v.EffectiveFromUtc <= nowUtc
                        && (v.EffectiveUntilUtc == null || v.EffectiveUntilUtc > nowUtc)
                    )
                    .OrderByDescending(v => v.EffectiveFromUtc)
                    .FirstOrDefault()
            );
    }

    private sealed class FakeAuthAuditWriter : IAuthAuditWriter
    {
        public Task AddAsync(AuthAuditLog log, CancellationToken ct = default) => Task.CompletedTask;
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
