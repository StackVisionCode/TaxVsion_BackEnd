using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Un servicio que muere tiene que volver solo. Wolverine no arranca sin RabbitMQ: reintenta 20 veces (~3 min) y
/// el proceso termina; sin <c>restart</c> el contenedor quedaba muerto hasta el próximo deploy, y tras reiniciar la
/// VM no subía ninguna API. Con <c>unless-stopped</c> Docker lo relanza hasta que el broker responde, y el outbox
/// en SQL entrega lo pendiente.
///
/// <para>Los de una sola vez quedan fuera: con <c>restart</c>, <c>db-ready</c> se relanzaría sin fin después de
/// terminar bien. Los <c>docker compose run --rm</c> del deploy ignoran el <c>restart</c> del servicio.</para>
/// </summary>
public sealed class ComposeRestartPolicyWiringTests
{
    private static readonly HashSet<string> OneShot = ["db-ready", "minio-provision", "migrations"];

    [Fact]
    public void Every_long_running_service_restarts_on_its_own()
    {
        var offenders = Services(ReadCompose())
            .Where(service => !OneShot.Contains(service.Name))
            .Where(service => RestartPolicy(service.Block) is not ("unless-stopped" or "always"))
            .Select(service => service.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Estos servicios no se relanzan si mueren (falta `restart: unless-stopped`): "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void One_shot_services_do_not_restart()
    {
        var compose = ReadCompose();
        var offenders = Services(compose)
            .Where(service => OneShot.Contains(service.Name) && RestartPolicy(service.Block) is not null)
            .Select(service => service.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Un servicio de una sola vez no lleva restart: " + string.Join(", ", offenders)
        );
        Assert.Equal(OneShot.Count, Services(compose).Count(service => OneShot.Contains(service.Name)));
    }

    private static string? RestartPolicy(string block)
    {
        var match = Regex.Match(block, @"^    restart:\s*(?<policy>\S+)", RegexOptions.Multiline);
        return match.Success ? match.Groups["policy"].Value.Trim('"', '\'') : null;
    }

    private static List<(string Name, string Block)> Services(string compose)
    {
        var lines = compose.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line == "services:");
        var services = new List<(string, List<string>)>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            // Fin de la sección services: la siguiente clave de primer nivel (volumes:, networks:...).
            if (Regex.IsMatch(lines[i], @"^[a-z]"))
                break;
            var service = Regex.Match(lines[i], @"^  (?<name>[a-z0-9-]+):\s*$");
            if (service.Success)
                services.Add((service.Groups["name"].Value, []));
            else if (services.Count > 0)
                services[^1].Item2.Add(lines[i]);
        }
        return services.Select(s => (s.Item1, string.Join('\n', s.Item2))).ToList();
    }

    private static string ReadCompose()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "deploy", "docker", "docker-compose.yml");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("deploy/docker/docker-compose.yml");
    }
}
