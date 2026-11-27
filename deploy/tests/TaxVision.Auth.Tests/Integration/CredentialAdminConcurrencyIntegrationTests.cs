using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;
using CredentialTokenRepository = TaxVision.Auth.Infrastructure.Persistence.Repositories.CredentialTokenRepository;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Contra SQL Server: dos emisiones de la oficina a la vez para el mismo objetivo dejan un solo link pendiente
/// (03 §4.8). El candado de fila del objetivo las pone en fila; la segunda supersede a la primera.
/// </summary>
public sealed class CredentialAdminConcurrencyIntegrationTests : IAsyncLifetime
{
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
            "ana-office@example.test",
            "hashed:right",
            UserActorType.TenantEmployee
        ).Value;
        user.VerifyEmail();
        _userId = user.Id;
        await using var db = _database.NewContext(_world.Tenant.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Two_concurrent_office_links_leave_a_single_pending_link()
    {
        using var barrier = new Barrier(2);

        await Task.WhenAll(
            Task.Run(() => IssueAsync(barrier, Guid.NewGuid())),
            Task.Run(() => IssueAsync(barrier, Guid.NewGuid()))
        );

        await using var read = _database.NewContext(_world.Tenant.Id);
        var links = await read.PasswordResetTokens.AsNoTracking().Where(t => t.UserId == _userId).ToListAsync();
        Assert.Equal(2, links.Count);
        var now = DateTime.UtcNow;
        Assert.Single(links, link => link.StatusAt(now) == PasswordResetTokenStatus.Pending);
        Assert.Single(links, link => link.RevocationReason == ResetRevocationReason.Superseded);
        Assert.All(links, link => Assert.Equal(ResetOrigin.Administrative, link.Origin));
    }

    private async Task IssueAsync(Barrier barrier, Guid actorId)
    {
        await using var db = _database.NewContext(_world.Tenant.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var target = await db.Users.SingleAsync(u => u.Id == _userId);
        var result = await PasswordResetIssuer.IssueByOfficeAsync(
            target,
            ResetOrigin.Administrative,
            actorId,
            new CredentialAdministrationOptions(),
            new CredentialTokenRepository(db),
            new TaxVision.Auth.Infrastructure.Security.SecureTokenService(),
            new AccountTokenProtectionTests.CapturingAccountTokenProtector(),
            _world.Tenants,
            new BarrierLock(barrier, new SqlUserLoginLock(db)),
            "127.0.0.1",
            "corr",
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    /// <summary>Las dos emisiones llegan juntas al candado.</summary>
    private sealed class BarrierLock(Barrier barrier, IUserLoginLock inner) : IUserLoginLock
    {
        public Task AcquireAsync(User user, CancellationToken ct)
        {
            barrier.SignalAndWait(BarrierTimeout);
            return inner.AcquireAsync(user, ct);
        }
    }
}
