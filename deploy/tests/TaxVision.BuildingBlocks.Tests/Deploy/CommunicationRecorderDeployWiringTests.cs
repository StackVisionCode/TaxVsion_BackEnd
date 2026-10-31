using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Contrato entre archivos del recorder de grabaciones (F7): su cuenta MinIO scoped, el secreto que la
/// sostiene, el bloque de compose y el gate de tests del workflow. Si falta una pieza, compose no
/// arranca (secreto requerido con <c>:?</c>) o el recorder sube con credenciales que MinIO rechaza.
/// </summary>
public sealed class CommunicationRecorderDeployWiringTests
{
    private const string RequiredSecret = "${RECORDER_MINIO_SECRET:?Set RECORDER_MINIO_SECRET in .env}";
    private const string SpoolDir = "/var/spool/communication-recorder";

    [Fact]
    public void Recorder_secret_is_required_by_the_recorder_and_by_minio_provision()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains(
            $"RECORDER_MINIO_SECRET_KEY: {RequiredSecret}",
            ServiceBlock(compose, "communication-recorder"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"RECORDER_MINIO_SECRET: {RequiredSecret}",
            ServiceBlock(compose, "minio-provision"),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Recorder_account_is_provisioned_with_its_own_scoped_policy()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var script = ReadRepoFile("deploy/docker/minio/provision-service-accounts.sh");
        var policy = ReadRepoFile("deploy/docker/minio/policies/recording-source.json");

        Assert.Contains(
            "RECORDER_MINIO_ACCESS_KEY: communication-recorder",
            ServiceBlock(compose, "communication-recorder"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "provision \"recording\" \"communication-recorder\" \"${RECORDER_MINIO_SECRET:?Set RECORDER_MINIO_SECRET}\"",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains("policies/recording-source.json", script, StringComparison.Ordinal);
        Assert.Contains("arn:aws:s3:::taxvision-temp/recording/*", policy, StringComparison.Ordinal);
        // Solo sube: nunca lee ni borra objetos de ningún bucket.
        Assert.DoesNotContain("s3:GetObject", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:DeleteObject", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("s3:*", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_publishes_no_ports_and_declares_its_healthcheck_and_spool()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var block = ServiceBlock(compose, "communication-recorder");

        // El RTP va de contenedor a contenedor: publicar el rango lo abriría a internet.
        Assert.False(
            Regex.IsMatch(block, @"^    ports:\s*$", RegexOptions.Multiline),
            "communication-recorder no debe publicar puertos al host."
        );
        Assert.Matches(new Regex(@"^    healthcheck:\s*$", RegexOptions.Multiline), block);
        Assert.Contains("http://localhost:5360/health", block, StringComparison.Ordinal);
        Assert.Contains($"- recorder-spool:{SpoolDir}", block, StringComparison.Ordinal);
        Assert.Contains($"RECORDER_SPOOL_DIR: {SpoolDir}", block, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^  recorder-spool:\s*$", RegexOptions.Multiline), compose);
    }

    [Fact]
    public void Spool_volume_is_mounted_where_the_image_gives_the_runtime_user_write_access()
    {
        // El contenedor corre como `taxvision`, no root. Un volumen montado en una ruta que la imagen no
        // crea queda de root y el recorder no puede escribir las pistas: falla solo en producción.
        var dockerfile = ReadRepoFile("src/Services/CommunicationRecorder/Dockerfile");

        Assert.Contains($"ENV RECORDER_SPOOL_DIR={SpoolDir}", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"mkdir -p {SpoolDir}", dockerfile, StringComparison.Ordinal);
        Assert.Matches(new Regex($@"chown -R taxvision:taxvision .*{Regex.Escape(SpoolDir)}"), dockerfile);
        Assert.Contains("USER taxvision", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_api_points_to_the_recorder_over_the_internal_network()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var communication = ServiceBlock(compose, "communication-api");
        var recorder = ServiceBlock(compose, "communication-recorder");

        Assert.Contains(
            "COMMUNICATION_RECORDER_BASE_URL: http://communication-recorder:5360",
            communication,
            StringComparison.Ordinal
        );
        Assert.Contains("RECORDER_PORT: \"5360\"", recorder, StringComparison.Ordinal);
        // Fotos del slate: el recorder arma la URL con su config; la petición solo trae el fileId.
        Assert.Contains("RECORDER_CLOUDSTORAGE_BASE_URL: http://cloudstorage-api:8080", recorder, StringComparison.Ordinal);
        // El recorder solo acepta el JWT de servicio que Communication ya usa.
        Assert.Contains(
            "RECORDER_ALLOWED_CLIENT_IDS: ${COMMUNICATION_SERVICE_AUTH_CLIENT_ID:-communication-worker}",
            recorder,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "COMMUNICATION_SERVICE_AUTH_CLIENT_ID: ${COMMUNICATION_SERVICE_AUTH_CLIENT_ID:-communication-worker}",
            communication,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Workflow_runs_the_recorder_node_tests_before_deploying()
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml").Replace("\r\n", "\n");

        Assert.Contains(
            "- name: Test CommunicationRecorder (Node)\n        working-directory: src/Services/CommunicationRecorder",
            workflow,
            StringComparison.Ordinal
        );
    }

    private static string ServiceBlock(string compose, string service)
    {
        var lines = compose.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        Assert.True(start >= 0, $"No se encontró el bloque del servicio '{service}' en docker-compose.yml.");

        var end = Array.FindIndex(lines, start + 1, l => Regex.IsMatch(l, @"^  [a-z0-9-]+:\s*$"));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
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
