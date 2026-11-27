using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.RefreshTokens;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

public sealed class ChangePasswordHandlerTests
{
    [Fact]
    public async Task Changing_the_password_revokes_pending_reset_links()
    {
        var world = new AccountSessionFixture();
        var credentials = new InMemoryCredentialTokenRepository();
        var pending = PasswordResetToken.Create(
            world.Admin.TenantId,
            world.Admin.Id,
            new string('a', 64),
            "127.0.0.1",
            TimeSpan.FromMinutes(30)
        );
        credentials.PasswordResets.Add(pending);

        var result = await ChangePasswordHandler.Handle(
            new ChangePasswordCommand(world.Admin.Id, Guid.NewGuid(), "secret", "a-brand-new-passphrase"),
            world.Users,
            credentials,
            new PrefixedPasswordHasher(),
            world.Tenants,
            new SessionAccessCutoff(world.Sessions, world.Denylist, new RecordingSessionRevocationPublisher()),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(pending.IsUsable(DateTime.UtcNow));
        Assert.NotNull(pending.RevokedAtUtc);
        Assert.Equal(ResetRevocationReason.PasswordChanged, pending.RevocationReason);
    }

    // I-2/I-3: con la cuenta asegurada el cambio no pasa (solo un link la recupera).
    [Fact]
    public async Task A_secured_account_cannot_change_its_password()
    {
        var world = new AccountSessionFixture();
        world.Admin.Secure();

        var result = await ChangeAsync(world, Guid.NewGuid());

        Assert.Equal("Auth.Invalid", result.Error.Code);
        Assert.Equal("hashed:secret", world.Admin.PasswordHash);
    }

    // 03 §10: la nueva no puede ser igual a la actual.
    [Fact]
    public async Task The_new_password_cannot_be_the_current_one()
    {
        var world = new AccountSessionFixture();
        world.Admin.RequireChange();

        var result = await ChangeAsync(world, Guid.NewGuid(), newPassword: "secret");

        Assert.Equal("User.Password", result.Error.Code);
        Assert.Equal(TaxVision.Auth.Domain.Users.CredentialStatus.ChangeRequired, world.Admin.CredentialStatus);
    }

    // Desde el token restringido: el cambio limpia el cambio obligatorio.
    [Fact]
    public async Task Changing_the_password_clears_a_required_change()
    {
        var world = new AccountSessionFixture();
        world.Admin.RequireChange();

        var result = await ChangeAsync(world, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(TaxVision.Auth.Domain.Users.CredentialStatus.Active, world.Admin.CredentialStatus);
    }

    private static Task<BuildingBlocks.Results.Result> ChangeAsync(
        AccountSessionFixture world,
        Guid sessionId,
        string newPassword = "a-brand-new-passphrase"
    ) =>
        ChangePasswordHandler.Handle(
            new ChangePasswordCommand(world.Admin.Id, sessionId, "secret", newPassword),
            world.Users,
            new InMemoryCredentialTokenRepository(),
            new PrefixedPasswordHasher(),
            world.Tenants,
            new SessionAccessCutoff(world.Sessions, world.Denylist, new RecordingSessionRevocationPublisher()),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

    // Cambio autenticado: corte en tiempo real de las otras sesiones; la actual sigue viva.
    [Fact]
    public async Task Changing_the_password_cuts_the_other_sessions_in_real_time_and_keeps_the_current_one()
    {
        var world = new AccountSessionFixture();
        var (current, _) = await world.StartWorkspaceSessionAsync();
        var other = await world.Issuer.StartSessionAsync(world.Admin, "UTC", [], ["pwd"], null, SessionSurface.Mobile);
        var revocations = new RecordingSessionRevocationPublisher();

        var result = await ChangePasswordHandler.Handle(
            new ChangePasswordCommand(world.Admin.Id, current, "secret", "a-brand-new-passphrase"),
            world.Users,
            new InMemoryCredentialTokenRepository(),
            new PrefixedPasswordHasher(),
            world.Tenants,
            new SessionAccessCutoff(world.Sessions, world.Denylist, revocations),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal((other.SessionId, "password_change"), Assert.Single(revocations.Published));
        Assert.Contains(other.SessionId, world.Denylist.Denied);
        Assert.DoesNotContain(current, world.Denylist.Denied);
    }
}
