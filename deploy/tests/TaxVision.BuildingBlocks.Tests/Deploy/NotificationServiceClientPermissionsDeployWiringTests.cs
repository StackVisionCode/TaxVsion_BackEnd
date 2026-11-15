using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Mínimo privilegio del cliente M2M de Notification: solo lo que su código usa (adjuntos en CloudStorage y
/// plantillas en Scribe). Con un permiso de más, su secreto filtrado serviría para enviar SMS cobrando el wallet
/// de una oficina o para tocar catálogo e inventario.
/// </summary>
public sealed class NotificationServiceClientPermissionsDeployWiringTests
{
    private static readonly string[] Expected =
    [
        "cloudstorage.file.download",
        "cloudstorage.file.upload",
        "cloudstorage.file.view",
        "scribe.render",
    ];

    [Fact]
    public void The_notification_client_has_only_the_permissions_its_code_uses()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var index = Regex
            .Match(compose, @"ServiceAuth__Clients__(?<n>\d+)__ClientId: \$\{NOTIFICATION_SERVICE_CLIENT_ID[:}]")
            .Groups["n"]
            .Value;
        Assert.False(string.IsNullOrEmpty(index), "No se encontró el cliente de Notification en el compose.");

        var permissions = Regex
            .Matches(compose, $@"ServiceAuth__Clients__{index}__Permissions__(?<k>\d+): (?<perm>\S+)")
            .Select(match => (Position: int.Parse(match.Groups["k"].Value), Permission: match.Groups["perm"].Value))
            .ToList();

        Assert.Equal(Expected, permissions.Select(p => p.Permission).Order());
        // Índices seguidos desde 0: el binder de configuración arma la lista en ese orden.
        Assert.Equal(Enumerable.Range(0, permissions.Count), permissions.Select(p => p.Position).Order());
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
