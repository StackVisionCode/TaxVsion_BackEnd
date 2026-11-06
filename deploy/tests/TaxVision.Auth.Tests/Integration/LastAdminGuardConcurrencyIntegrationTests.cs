using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (AUTH_DB_CONNECTION o user-secrets de Auth): dos bajas de admins en paralelo
/// cuentan a la vez. Sin candado, las dos veían 2 admins y la oficina quedaba sin ninguno; con el candado
/// del tenant, la segunda cuenta después de que la primera confirma.
/// </summary>
public sealed class LastAdminGuardConcurrencyIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(3);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("AUTH_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("c9f518b8-3374-4abd-a7b6-f0b20cb0877f")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set AUTH_DB_CONNECTION or the Auth user-secrets to run this test.");

    private User _adminA = null!;
    private User _adminB = null!;

    public async Task InitializeAsync()
    {
        _adminA = NewUser("Ana", UserActorType.TenantAdmin);
        _adminB = NewUser("Beto", UserActorType.TenantAdmin);
        await using var db = NewContext();
        db.Users.AddRange(_adminA, _adminB);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoAdminsDeactivatingEachOther_InParallel_LeavesOneActiveAdmin()
    {
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => DeactivateAsync(_adminA.Id, _adminB.Id, barrier)),
            Task.Run(() => DeactivateAsync(_adminB.Id, _adminA.Id, barrier))
        );

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.IsFailure && r.Error.Code == "User.LastAdmin");
        Assert.True(await CountActiveAdminsAsync() >= 1);
    }

    [Fact]
    public async Task TwoOffboardsOfDifferentAdmins_InParallel_LeavesOneActiveAdmin()
    {
        // Quien retira es un tercero (empleado con users.manage): no cuenta como admin activo.
        using var barrier = new Barrier(2);
        var requester = Guid.NewGuid();

        var results = await Task.WhenAll(
            Task.Run(() => OffboardAsync(requester, _adminA.Id, barrier)),
            Task.Run(() => OffboardAsync(requester, _adminB.Id, barrier))
        );

        Assert.Single(results, r => r.IsFailure && r.Error.Code == "User.LastAdmin");
        Assert.True(await CountActiveAdminsAsync() >= 1);
    }

    private async Task<Result> DeactivateAsync(Guid requester, Guid target, Barrier barrier)
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await DeactivateUserHandler.Handle(
            new DeactivateUserCommand(_tenantId, target, requester, UserActorType.TenantAdmin),
            new BarrierUserRepository(new UserRepository(db), barrier),
            new SqlTenantAdminLock(db, Options.Create(new TenantAdminLockOptions())),
            new EmptyUserSessionRepository(),
            new RecordingAccessTokenDenylist(),
            new QuietRevocations(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            db,
            new FakeMessageBus(),
            CancellationToken.None
        );
        await transaction.CommitAsync();
        return result;
    }

    private async Task<Result> OffboardAsync(Guid requester, Guid target, Barrier barrier)
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await OffboardUserHandler.Handle(
            new OffboardUserCommand(_tenantId, target, requester, SuccessorUserId: null),
            new BarrierUserRepository(new UserRepository(db), barrier),
            new SqlTenantAdminLock(db, Options.Create(new TenantAdminLockOptions())),
            new InMemoryMfaRepository(),
            new InMemoryInvitationRepository(),
            new InMemoryOffboardingRunRepository(),
            new EmptyUserSessionRepository(),
            new RecordingAccessTokenDenylist(),
            new QuietRevocations(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            db,
            new FakeMessageBus(),
            CancellationToken.None
        );
        await transaction.CommitAsync();
        return result;
    }

    private async Task<int> CountActiveAdminsAsync()
    {
        await using var db = NewContext();
        return await db
            .Users.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == _tenantId && u.IsActive && u.ActorType == UserActorType.TenantAdmin);
    }

    private User NewUser(string name, UserActorType actorType) =>
        User.Register(_tenantId, name, "Test", $"{Guid.NewGuid():N}@example.test", "hash", actorType).Value;

    private AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class QuietRevocations : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    /// <summary>Repositorio real que espera al otro hilo justo después de contar, para forzar la carrera.</summary>
    private sealed class BarrierUserRepository(IUserRepository inner, Barrier barrier) : IUserRepository
    {
        public Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<PagedResult<TenantOwnerReconciliationResponse>> ListTenantOwnersForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<TenantOwnerReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<CustomerPortalAccountReconciliationResponse>> ListPortalAccountsForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<CustomerPortalAccountReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<StaffUserReconciliationResponse>> ListStaffUsersForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<StaffUserReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<UserDirectoryReconciliationResponse>> ListUsersForDirectoryReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<UserDirectoryReconciliationResponse>([], page, size, 0));

        public async Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default)
        {
            var count = await inner.CountActiveAdminsAsync(tenantId, ct);
            barrier.SignalAndWait(BarrierTimeout);
            return count;
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => inner.GetByEmailAsync(tenantId, email, kind, ct);

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => inner.EmailExistsAsync(tenantId, email, kind, ct);

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => inner.GetPortalUserByCustomerAsync(tenantId, customerId, ct);

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            inner.GetByOnboardingIdAsync(onboardingId, ct);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => inner.GetActiveTenantIdsByEmailAsync(email, kind, ct);

        public Task AddAsync(User user, CancellationToken ct = default) => inner.AddAsync(user, ct);

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            inner.CountActiveAsync(tenantId, ct);

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            inner.GetPrimaryAdminAsync(tenantId, ct);

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => inner.GetPagedAsync(tenantId, page, size, search, isActive, customerId, accountKind, ct);
    }
}
