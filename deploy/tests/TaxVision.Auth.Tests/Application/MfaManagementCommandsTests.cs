using TaxVision.Auth.Application.Mfa.Commands;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Con la cuenta asegurada, la contraseña vieja tampoco sirve para tocar el MFA (I-2).</summary>
public sealed class MfaManagementCommandsTests
{
    [Fact]
    public async Task Disabling_mfa_with_the_password_of_a_secured_account_is_refused()
    {
        var world = new AccountSessionFixture();
        var mfa = new InMemoryMfaRepository();
        mfa.Enroll(world.Admin);
        world.Admin.Secure();

        var result = await DisableMfaHandler.Handle(
            new DisableMfaCommand(world.Admin.Id, "secret"),
            world.Users,
            mfa,
            new PrefixedPasswordHasher(),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal("Auth.Invalid", result.Error.Code);
        Assert.True(world.Admin.MfaEnabled);
    }

    [Fact]
    public async Task Regenerating_recovery_codes_with_the_password_of_a_secured_account_is_refused()
    {
        var world = new AccountSessionFixture();
        var mfa = new InMemoryMfaRepository();
        mfa.Enroll(world.Admin);
        world.Admin.Secure();

        var result = await RegenerateRecoveryCodesHandler.Handle(
            new RegenerateRecoveryCodesCommand(world.Admin.Id, "secret"),
            world.Users,
            mfa,
            new PrefixedPasswordHasher(),
            world.TokenService,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            CancellationToken.None
        );

        Assert.Equal("Auth.Invalid", result.Error.Code);
        Assert.Single(mfa.Codes);
    }
}
