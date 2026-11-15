using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Sin URL de Tenant, la reconciliación de zonas cae a http://localhost:5217: en local funciona y en el contenedor no
/// llega a nada (2026-11-15: Correspondence y FinancialInsights quedaron sin proyecciones en producción).
/// </summary>
public sealed class TenantTimeZoneReconciliationDeployTests
{
    [Fact]
    public void Every_service_that_reconciles_time_zones_gets_the_tenant_url_in_compose()
    {
        var root = RepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "deploy", "docker", "docker-compose.yml"))
            .Replace("\r\n", "\n");
        var blocks = Regex
            .Split(compose, @"^(?=  [a-z0-9_-]+:\s*$)", RegexOptions.Multiline)
            .Where(block => Regex.IsMatch(block, @"^  [a-z0-9_-]+:\s*$", RegexOptions.Multiline))
            .ToList();

        var checkedServices = new List<string>();
        var offenders = new List<string>();
        foreach (var block in blocks)
        {
            var dockerfile = Regex.Match(block, @"dockerfile:\s*src/Services/(?<service>[^/\s]+)/");
            if (!dockerfile.Success || !ReconcilesTimeZones(root, dockerfile.Groups["service"].Value))
                continue;

            var name = block.Split('\n')[0].Trim().TrimEnd(':');
            checkedServices.Add(name);
            var hasUrl =
                Regex.IsMatch(
                    block,
                    @"^\s+TenantTimeZones__Reconciliation__TenantBaseUrl:\s*http://tenant-api:8080",
                    RegexOptions.Multiline
                )
                || Regex.IsMatch(
                    block,
                    @"^\s+PlatformSettings__TenantBaseUrl:\s*http://tenant-api:8080",
                    RegexOptions.Multiline
                )
                || Regex.IsMatch(block, @"^\s+<<:\s*\*platform-settings-env\s*$", RegexOptions.Multiline);
            if (!hasUrl)
                offenders.Add(name);
        }

        Assert.True(
            checkedServices.Count >= 12,
            $"Solo se revisaron {checkedServices.Count} servicios con reconciliación de zonas."
        );
        Assert.True(
            offenders.Count == 0,
            "Servicio que reconcilia zonas sin URL de Tenant en el compose (caería a localhost):\n"
                + string.Join("\n", offenders)
        );
    }

    private static bool ReconcilesTimeZones(string root, string service)
    {
        var dir = Path.Combine(root, "src", "Services", service);
        return Directory.Exists(dir)
            && Directory
                .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal
                    )
                )
                .Any(path =>
                    File.ReadAllText(path).Contains("AddTenantTimeZoneReconciliation<", StringComparison.Ordinal)
                );
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("TaxVision.slnx");
    }
}
