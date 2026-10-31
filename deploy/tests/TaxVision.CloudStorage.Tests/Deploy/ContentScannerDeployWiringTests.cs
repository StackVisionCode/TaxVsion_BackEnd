using System.Text.RegularExpressions;

namespace TaxVision.CloudStorage.Tests.Deploy;

/// <summary>
/// T-20: contrato entre compose, workflow y Dockerfile del content-scanner. Sin la clave en los dos
/// lados CloudStorage recibe 401 y todo queda en revisión; con un puerto publicado en producción el
/// scanner quedaría expuesto a internet; sin el hash fijado la imagen podría traer otro modelo.
/// </summary>
public sealed class ContentScannerDeployWiringTests
{
    private const string RequiredKey = "${CONTENT_SCANNER_API_KEY:?Set CONTENT_SCANNER_API_KEY in .env}";

    [Fact]
    public void Production_scanner_lives_only_on_the_internal_network_and_requires_the_key()
    {
        var block = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.yml"), "content-scanner");

        Assert.False(
            Regex.IsMatch(block, @"^    ports:\s*$", RegexOptions.Multiline),
            "content-scanner no debe publicar puertos."
        );
        Assert.Contains($"ContentScanner__ApiKey: {RequiredKey}", block, StringComparison.Ordinal);
        Assert.Contains("http://localhost:8080/health/ready", block, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^    mem_limit: \d+m\s*$", RegexOptions.Multiline), block);
    }

    [Fact]
    public void CloudStorage_calls_the_scanner_by_its_internal_name_with_the_same_key()
    {
        var block = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.yml"), "cloudstorage-api");

        Assert.Contains("ContentScanner__BaseUrl: http://content-scanner:8080", block, StringComparison.Ordinal);
        Assert.Contains($"ContentScanner__ApiKey: {RequiredKey}", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_scanner_is_published_only_on_loopback()
    {
        var block = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.local-infra.yml"), "content-scanner");

        Assert.Contains("- \"127.0.0.1:5540:8080\"", block, StringComparison.Ordinal);
        Assert.Contains($"ContentScanner__ApiKey: {RequiredKey}", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_pins_the_model_by_commit_and_hash_and_runs_without_root()
    {
        var dockerfile = ReadRepoFile("src/Services/ContentScanner/Dockerfile");

        Assert.Matches(new Regex(@"ARG MODEL_URL=https://huggingface\.co/.+/resolve/[0-9a-f]{40}/"), dockerfile);
        Assert.Matches(new Regex(@"ARG MODEL_SHA256=[0-9a-f]{64}"), dockerfile);
        Assert.Contains("sha256sum -c -", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER app", dockerfile, StringComparison.Ordinal);
    }

    private static string ServiceBlock(string compose, string service)
    {
        var lines = compose.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        Assert.True(start >= 0, $"No se encontró el bloque del servicio '{service}'.");

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
