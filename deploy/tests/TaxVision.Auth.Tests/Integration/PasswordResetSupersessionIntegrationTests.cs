using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Contra SQL Server: dos pedidos de reset a la vez para la misma cuenta dejan un solo link utilizable (I-4).
/// El candado de fila del usuario pone en fila las emisiones; el segundo revoca al primero como Superseded.
/// </summary>
public sealed class PasswordResetSupersessionIntegrationTests : IAsyncLifetime
{
    private const string Email = "ana@example.test";
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(10);

    private readonly AccountSessionFixture _world = new();
    private AuthTestDatabase _database = null!;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        var user = User.Register(
            _world.Tenant.Id,
            "Ana",
            "Test",
            Email,
            "hashed:right",
            UserActorType.TenantEmployee
        ).Value;
        _userId = user.Id;
        await using var db = _database.NewContext(_world.Tenant.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Two_concurrent_forgot_requests_leave_a_single_pending_link()
    {
        using var barrier = new Barrier(2);

        await Task.WhenAll(Task.Run(() => ForgotAsync(barrier)), Task.Run(() => ForgotAsync(barrier)));

        await using var read = _database.NewContext(_world.Tenant.Id);
        var links = await read.PasswordResetTokens.AsNoTracking().Where(t => t.UserId == _userId).ToListAsync();
        Assert.Equal(2, links.Count);
        var now = DateTime.UtcNow;
        Assert.Single(links, link => link.StatusAt(now) == PasswordResetTokenStatus.Pending);
        Assert.Single(links, link => link.RevocationReason == ResetRevocationReason.Superseded);
    }

    private async Task ForgotAsync(Barrier barrier)
    {
        await using var db = _database.NewContext(_world.Tenant.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand(Email, UserAccountKind.Staff, _world.Tenant.Id),
            new UserRepository(db),
            new BarrierTenantRegistry(barrier, _world.Tenants),
            new CredentialTokenRepository(db),
            _world.TokenService,
            new NullAccountTokenProtector(),
            new SqlUserLoginLock(db),
            new PermissiveLoginThrottler(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            db,
            new FakeMessageBus(),
            Microsoft.Extensions.Options.Options.Create(
                new TaxVision.Auth.Application.Common.CredentialAdministrationOptions()
            ),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        await transaction.CommitAsync();
    }

    /// <summary>Cada pedido espera al otro en su primera lectura (la oficina del Host), antes de tocar la cuenta.</summary>
    private sealed class BarrierTenantRegistry(Barrier barrier, ITenantRegistry inner) : ITenantRegistry
    {
        private bool _waited;

        public Task<TaxVision.Auth.Domain.Tenants.Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (!_waited)
            {
                _waited = true;
                barrier.SignalAndWait(BarrierTimeout);
            }
            return inner.GetByIdAsync(tenantId, ct);
        }

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            BuildingBlocks.Tenancy.TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
