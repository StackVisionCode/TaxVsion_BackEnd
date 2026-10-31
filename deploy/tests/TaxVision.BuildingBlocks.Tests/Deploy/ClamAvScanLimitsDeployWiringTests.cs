using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Grabaciones server-side (F7): con los límites por defecto de clamd (100M) y el timeout de 120 s del
/// escáner, un MP4 de más de ~100 MB terminaba en <c>scan_error</c>. El compose sube los tres límites
/// de clamd antes de ceder al ENTRYPOINT original (<c>/init</c>) y el timeout de CloudStorage al tope.
/// </summary>
public sealed class ClamAvScanLimitsDeployWiringTests
{
    [Theory]
    [InlineData("StreamMaxLength", "CLAMD_STREAM_MAX_LENGTH")]
    [InlineData("MaxFileSize", "CLAMD_MAX_FILE_SIZE")]
    [InlineData("MaxScanSize", "CLAMD_MAX_SCAN_SIZE")]
    public void Clamav_rewrites_each_limit_from_its_variable_before_starting(string option, string variable)
    {
        var block = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.yml"), "clamav");

        Assert.Contains($"echo \"{option} $${{{variable}:-2100M}}\"", block, StringComparison.Ordinal);
        Assert.Contains($"{variable}: ${{{variable}:-2100M}}", block, StringComparison.Ordinal);
        Assert.Contains(option, Regex.Match(block, @"sed -i -E '[^']+'").Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Clamav_still_hands_over_to_the_image_init_and_keeps_its_healthcheck()
    {
        var block = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.yml"), "clamav");

        Assert.Contains("exec /init", block, StringComparison.Ordinal);
        Assert.Contains("clamdscan --ping 1", block, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudStorage_scanner_timeout_is_raised_to_the_clamp_ceiling()
    {
        var block = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.yml"), "cloudstorage-api");

        Assert.Contains("ClamAv__TimeoutSeconds: \"600\"", block, StringComparison.Ordinal);
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
