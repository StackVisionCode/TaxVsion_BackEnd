using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Cada cliente M2M trae un default <c>BaseUrl = "http://localhost:NNNN"</c>. En Docker lo pisa el
/// compose, así que producción nunca lo nota; en la flota local no lo pisa nadie, y si el puerto no
/// es el que ese servicio escucha, la llamada muere con "connection refused".
///
/// <para>Lo que lo hace difícil de ver es que el síntoma no se parece a la causa: el cliente captura
/// la excepción y devuelve un <c>Result.Failure</c>, que llega al navegador como un <b>400</b>. El
/// 2026-10-02, emitir una factura daba 400 porque Billing llamaba a Inventory en el 5180 y escucha en
/// el 5490. Parecía un error de validación. Se encontraron 8 más iguales.</para>
///
/// <para>Este test compara los defaults contra <c>scripts/start-fleet.ps1</c>, que es la fuente de
/// verdad de qué puerto escucha cada servicio en dev.</para>
/// </summary>
public sealed class DevClientPortDefaultsTests
{
    /// <summary>
    /// Puertos que NO son de un servicio .NET de la flota y por eso no se comparan: 4200/4300 son los
    /// frontends (`ng serve`) y 5350 es Communication (Node, fuera de start-fleet.ps1).
    /// </summary>
    private static readonly HashSet<string> NotFleetServices = ["4200", "4300", "5350"];

    [Fact]
    public void Every_localhost_default_points_at_a_port_some_service_actually_listens_on()
    {
        var listening = FleetPorts();
        Assert.NotEmpty(listening);

        var offenders = new List<string>();
        foreach (var file in ClientOptionFiles())
        {
            foreach (
                Match match in Regex.Matches(
                    File.ReadAllText(file),
                    @"(?<prop>\w*Url)\s*\{\s*get;\s*set;\s*\}\s*=\s*""http://localhost:(?<port>\d+)"""
                )
            )
            {
                var port = match.Groups["port"].Value;
                if (NotFleetServices.Contains(port) || listening.ContainsKey(port))
                    continue;

                offenders.Add(
                    $"{Path.GetFileName(file)} ({match.Groups["prop"].Value}) -> localhost:{port}, "
                        + "donde no escucha ningún servicio de la flota"
                );
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Defaults de dev apuntando a un puerto muerto. En Docker no se nota (lo pisa el compose), "
                + "pero en la flota local la llamada da connection refused y el usuario ve un 400:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    /// <summary>Puerto -> nombre del servicio, leído de scripts/start-fleet.ps1.</summary>
    private static Dictionary<string, string> FleetPorts()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "start-fleet.ps1");
        Assert.True(File.Exists(script), $"No se encontró {script}.");

        var ports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(File.ReadAllText(script), @"n = ""(?<name>\w+)"";.*?p = (?<port>\d+)"))
            ports[match.Groups["port"].Value] = match.Groups["name"].Value;

        ports["5047"] = "Gateway";
        return ports;
    }

    private static IEnumerable<string> ClientOptionFiles() =>
        Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            );

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("No se pudo localizar la raíz del repo (TaxVision.slnx).");

        return dir.FullName;
    }
}
