using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Infrastructure.Security;
using Xunit;

namespace TaxVision.Signature.Tests.Security;

/// <summary>
/// La clave de los enlaces de firma: fija desde archivo (los enlaces sobreviven a un reinicio) y nunca una
/// efímera fuera de Development, que es lo que tumbaba todos los enlaces en cada redeploy de producción.
/// </summary>
public sealed class RsaSigningKeyProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("signer-key-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_link_signed_before_a_restart_still_verifies_after_it_with_the_same_key_file()
    {
        var pem = WriteFile("signer.pem", NewPrivateKeyPem(2048));
        var payload = new SigningTokenPayload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            DateTime.UtcNow.AddDays(7),
            Guid.NewGuid().ToString("N")
        );

        string token;
        using (var beforeRestart = Provider(pem, Environments.Production))
            token = new SigningTokenService(beforeRestart).Issue(payload);

        using var afterRestart = Provider(pem, Environments.Production);
        var verified = new SigningTokenService(afterRestart).Verify(token);

        Assert.True(verified.IsSuccess);
        Assert.Equal(payload.SignerId, verified.Value.SignerId);
    }

    [Fact]
    public void The_same_key_file_always_publishes_the_same_kid()
    {
        var pem = WriteFile("signer.pem", NewPrivateKeyPem(2048));

        using var first = Provider(pem, Environments.Production);
        using var second = Provider(pem, Environments.Production);

        Assert.Equal(first.ActiveKid, second.ActiveKid);
    }

    [Fact]
    public void Outside_development_a_missing_key_stops_the_service_instead_of_using_an_ephemeral_one()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Provider(null, Environments.Production));

        Assert.Contains("PrivateKeyPem is not configured", error.Message);
    }

    [Fact]
    public void In_development_a_missing_key_falls_back_to_an_ephemeral_one()
    {
        using var provider = Provider(null, Environments.Development);

        Assert.NotEmpty(provider.ActiveKid);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void A_configured_path_that_does_not_exist_is_a_configuration_error_in_every_environment(string env)
    {
        var missing = Path.Combine(_dir, "nope.pem");

        var error = Assert.Throws<InvalidOperationException>(() => Provider(missing, env));

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void A_file_that_is_not_a_pem_key_is_rejected()
    {
        var pem = WriteFile("broken.pem", "not a key");

        Assert.Throws<InvalidOperationException>(() => Provider(pem, Environments.Production));
    }

    [Fact]
    public void A_public_key_alone_is_rejected_because_it_cannot_sign()
    {
        using var rsa = RSA.Create(2048);
        var pem = WriteFile("public.pem", rsa.ExportSubjectPublicKeyInfoPem());

        var error = Assert.Throws<InvalidOperationException>(() => Provider(pem, Environments.Production));

        Assert.Contains("private key", error.Message);
    }

    [Fact]
    public void A_key_shorter_than_2048_bits_is_rejected()
    {
        var pem = WriteFile("short.pem", NewPrivateKeyPem(1024));

        Assert.Throws<InvalidOperationException>(() => Provider(pem, Environments.Production));
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string NewPrivateKeyPem(int bits)
    {
        using var rsa = RSA.Create(bits);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static RsaSigningKeyProvider Provider(string? pemPath, string environmentName) =>
        new(
            Options.Create(new SignerJwtOptions { PrivateKeyPem = pemPath }),
            new StubHostEnvironment(environmentName),
            NullLogger<RsaSigningKeyProvider>.Instance
        );

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TaxVision.Signature.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
