using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// Detrás de Cloudflare → Caddy → Gateway (YARP) → microservicio, la IP del socket es la del
/// contenedor (172.x en Docker), NO la del cliente. Si un web host no resuelve la IP real, la guarda
/// mal en certificados de firma, audit logs, sesiones y el particionado del rate limiter — y nada falla.
///
/// <para>
/// Esta fitness function obliga a que TODO punto de entrada web (cada <c>Program.cs</c> con
/// <c>WebApplication.CreateBuilder</c>) registre la resolución compartida
/// (<c>AddTaxVisionClientIpForwarding</c> + <c>UseTaxVisionClientIp</c>). Así ningún microservicio
/// nuevo vuelve a guardar la IP de Docker por olvido.
/// </para>
/// </summary>
public sealed class ClientIpForwardingFitnessTests
{
    [Fact]
    public void Every_web_host_resolves_the_real_client_ip()
    {
        var offenders = new List<string>();

        foreach (var file in WebHostProgramFiles())
        {
            var source = File.ReadAllText(file);
            var registers = source.Contains("AddTaxVisionClientIpForwarding", StringComparison.Ordinal);
            var uses = source.Contains("UseTaxVisionClientIp", StringComparison.Ordinal);
            if (!registers || !uses)
            {
                var missing =
                    (registers ? "" : "AddTaxVisionClientIpForwarding ") + (uses ? "" : "UseTaxVisionClientIp");
                offenders.Add($"{RelativePath(file)}: falta {missing.Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Web hosts que no resuelven la IP real del cliente (guardarían la IP de Docker):\n  "
                + string.Join("\n  ", offenders.Order())
        );
    }

    private static IEnumerable<string> WebHostProgramFiles()
    {
        var root = RepositoryRoot();
        foreach (var area in new[] { "src/Services", "src/Gateway" })
        {
            var path = Path.Combine(root, area);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "Program.cs", SearchOption.AllDirectories))
            {
                if (
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                )
                    continue;

                // Solo puntos de entrada web reales (los que arrancan un WebApplication).
                if (File.ReadAllText(file).Contains("WebApplication.CreateBuilder", StringComparison.Ordinal))
                    yield return file;
            }
        }
    }

    private static string RelativePath(string file)
    {
        var root = RepositoryRoot();
        return file.StartsWith(root, StringComparison.Ordinal) ? file[(root.Length + 1)..] : file;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
