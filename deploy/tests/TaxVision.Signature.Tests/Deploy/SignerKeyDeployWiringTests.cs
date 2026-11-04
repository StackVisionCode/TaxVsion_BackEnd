using Xunit;

namespace TaxVision.Signature.Tests.Deploy;

/// <summary>
/// La clave de los enlaces de firma tiene que llegar al contenedor de producción: sin ella Signature no
/// arranca. El secret vive en `production-extra`, que solo lee el job `deploy`; escrito en `stage` llega vacío.
/// </summary>
public sealed class SignerKeyDeployWiringTests
{
    private const string SecretFile = "signature-signer-jwt.pem";

    [Fact]
    public void The_deploy_job_writes_the_signer_key_file_from_the_production_extra_secret()
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");
        var deployJob = workflow.IndexOf("environment: production-extra", StringComparison.Ordinal);
        var secretRef = workflow.IndexOf("${{ secrets.SIGNATURE_SIGNER_JWT_PEM_B64 }}", StringComparison.Ordinal);
        var write = workflow.IndexOf($"$TAXVISION_SECRETS_DIR/{SecretFile}\"", StringComparison.Ordinal);

        Assert.True(deployJob > 0, "The deploy job (environment: production-extra) was not found.");
        Assert.True(secretRef > deployJob, "SIGNATURE_SIGNER_JWT_PEM_B64 must be read by the deploy job.");
        Assert.True(write > deployJob, $"The deploy job must write {SecretFile}.");
    }

    [Fact]
    public void The_signature_container_gets_the_key_path_and_the_mounted_file()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains($"Signature__SignerJwt__PrivateKeyPem: /run/secrets/{SecretFile}", compose);
        Assert.Contains(
            $"${{TAXVISION_SECRETS_DIR:-/opt/taxvision/secrets}}/{SecretFile}:/run/secrets/{SecretFile}:ro",
            compose
        );
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
