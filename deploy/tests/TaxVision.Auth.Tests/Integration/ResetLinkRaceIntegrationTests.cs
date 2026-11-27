using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Contra SQL Server: canje y cancelación del mismo link a la vez (03 §18.4). El perdedor responde un error de
/// dominio (409 <c>LinkAlreadyUsed</c> o el genérico del token), nunca 500, y la contraseña no cambia si gana la
/// cancelación.
/// </summary>
public sealed class ResetLinkRaceIntegrationTests : IAsyncLifetime
{
    private const string OriginalHash = "hashed:right";

    private readonly AccountSessionFixture _world = new();
    private AuthTestDatabase _database = null!;
    private Guid _ownerId;
    private Guid _userId;
    private Guid _linkId;
    private string _rawToken = string.Empty;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        var owner = User.Register(
            _world.Tenant.Id,
            "Olga",
            "Owner",
            "owner-race@example.test",
            OriginalHash,
            UserActorType.TenantAdmin,
            null,
            Guid.NewGuid()
        ).Value;
        owner.VerifyEmail();
        var user = User.Register(
            _world.Tenant.Id,
            "Ana",
            "Test",
            "ana-race@example.test",
            OriginalHash,
            UserActorType.TenantEmployee
        ).Value;
        user.VerifyEmail();
        _rawToken = _world.TokenService.GenerateToken();
        var link = PasswordResetToken.Create(
            _world.Tenant.Id,
            user.Id,
            _world.TokenService.Hash(_rawToken),
            null,
            TimeSpan.FromHours(1),
            ResetOrigin.Administrative,
            owner.Id
        );
        (_ownerId, _userId, _linkId) = (owner.Id, user.Id, link.Id);
        await using var db = _database.NewContext(_world.Tenant.Id);
        db.Users.AddRange(owner, user);
        db.PasswordResetTokens.Add(link);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task When_the_cancel_commits_first_the_redeem_gets_the_invalid_token_error()
    {
        // La cancelación entra y confirma después de que el canje leyó el link.
        var tenants = new HookedTenantRegistry(
            async () => Assert.True((await CancelAsync(new FakeAuthAuditWriter())).IsSuccess),
            _world.Tenants
        );

        var redeem = await RedeemAsync(tenants);

        Assert.Equal("Auth.InvalidResetToken", redeem.Error.Code);
        await using var read = _database.NewContext(_world.Tenant.Id);
        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == _userId);
        Assert.Equal(OriginalHash, user.PasswordHash);
        var link = await read.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == _linkId);
        Assert.Null(link.UsedAtUtc);
        Assert.Equal(ResetRevocationReason.CancelledByOffice, link.RevocationReason);
    }

    [Fact]
    public async Task When_the_link_is_used_while_the_cancel_saves_the_cancel_gets_link_already_used()
    {
        // El canje confirma entre la lectura de la cancelación y su guardado (choque de RowVersion).
        var audit = new HookedAuditWriter(UseLinkElsewhereAsync);

        var cancel = await CancelAsync(audit);

        Assert.Equal("Auth.CredentialAdmin.LinkAlreadyUsed", cancel.Error.Code);
        await using var read = _database.NewContext(_world.Tenant.Id);
        var link = await read.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == _linkId);
        Assert.NotNull(link.UsedAtUtc);
        Assert.Null(link.RevokedAtUtc);
    }

    private async Task UseLinkElsewhereAsync()
    {
        await using var db = _database.NewContext(_world.Tenant.Id);
        var link = await db.PasswordResetTokens.IgnoreQueryFilters().SingleAsync(t => t.Id == _linkId);
        link.MarkUsed();
        await db.SaveChangesAsync();
    }

    // Igual que el middleware de Wolverine: transacción, handler, guardado final y commit.
    private async Task<Result> RedeemAsync(ITenantRegistry tenants)
    {
        await using var db = _database.NewContext(_world.Tenant.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await ResetPasswordHandler.Handle(
            new ResetPasswordCommand(_rawToken, "a-brand-new-passphrase", _world.Tenant.Id),
            new CredentialTokenRepository(db),
            _world.TokenService,
            new UserRepository(db),
            tenants,
            new SessionAccessCutoff(
                new SessionRepository(db),
                _world.Denylist,
                new RecordingSessionRevocationPublisher()
            ),
            new MfaRepository(db),
            new PrefixedPasswordHasher(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            db,
            new SqlUserLoginLock(db),
            new FakeMessageBus(),
            CancellationToken.None
        );
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    private async Task<Result> CancelAsync(IAuthAuditWriter auditWriter)
    {
        await using var db = _database.NewContext(_world.Tenant.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var users = new UserRepository(db);
        var result = await OfficeCredentialAdministration.CancelAsync(
            _userId,
            await users.GetByIdAsync(_userId),
            new CredentialAdministrationOptions(),
            users,
            new CredentialTokenRepository(db),
            new SqlUserLoginLock(db),
            new CredentialAdminAudit(
                auditWriter,
                _world.Tenant.Id,
                _ownerId,
                "127.0.0.1",
                "UA",
                "corr",
                "TenantAdmin",
                UserAccountKind.Staff
            ),
            db,
            CancellationToken.None
        );
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>Corre <paramref name="hook"/> en la primera lectura de la oficina (después de leer el link).</summary>
    private sealed class HookedTenantRegistry(Func<Task> hook, ITenantRegistry inner) : ITenantRegistry
    {
        private bool _ran;

        public async Task<TaxVision.Auth.Domain.Tenants.Tenant?> GetByIdAsync(
            Guid tenantId,
            CancellationToken ct = default
        )
        {
            if (!_ran)
            {
                _ran = true;
                await hook();
            }
            return await inner.GetByIdAsync(tenantId, ct);
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

    /// <summary>Corre <paramref name="hook"/> en el primer registro de auditoría (justo antes del guardado).</summary>
    private sealed class HookedAuditWriter(Func<Task> hook) : IAuthAuditWriter
    {
        private bool _ran;

        public async Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
        {
            if (_ran)
                return;
            _ran = true;
            await hook();
        }
    }
}
