using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
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
            world.Sessions,
            world.Denylist,
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
    }
}
