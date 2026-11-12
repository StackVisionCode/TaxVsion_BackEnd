using TaxVision.Auth.Onboarding.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// El atajo de OTP fijo es solo de desarrollo local: sin opción el OTP es aleatorio, y con la opción puesta
/// en modo producción (resolución por Host activa) el arranque falla en vez de dejar un código fijo.
/// </summary>
public sealed class DevFixedOtpCodeGeneratorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_the_option_the_code_is_random(string? configured)
    {
        var generator = DevFixedOtpCodeGenerator.Resolve(configured, enforceHostResolution: true);

        Assert.IsType<NumericOtpCodeGenerator>(generator);
    }

    [Fact]
    public void With_the_option_in_local_mode_the_code_is_fixed()
    {
        var generator = DevFixedOtpCodeGenerator.Resolve(" 000000 ", enforceHostResolution: false);

        Assert.Equal("000000", generator.Generate());
        Assert.Equal("000000", generator.Generate());
    }

    [Fact]
    public void The_option_refuses_to_start_in_production_mode()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DevFixedOtpCodeGenerator.Resolve("000000", enforceHostResolution: true)
        );

        Assert.Contains("production", error.Message);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void A_malformed_code_is_rejected(string configured) =>
        Assert.Throws<InvalidOperationException>(() =>
            DevFixedOtpCodeGenerator.Resolve(configured, enforceHostResolution: false)
        );
}
