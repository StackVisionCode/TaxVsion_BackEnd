using System.Text.RegularExpressions;

namespace TaxVision.Notes.Tests.Deploy;

/// <summary>
/// Notes pide a CloudStorage la URL de sus adjuntos con su token de servicio: el cliente <c>notes-worker</c> de
/// <c>auth-api</c> tiene que llevar <c>cloudstorage.file.download</c>; sin él, toda descarga de una nota da 503.
/// </summary>
public sealed class NotesDownloadPermissionDeployWiringTests
{
    [Fact]
    public void The_notes_worker_client_of_auth_api_can_download_files()
    {
        var authApi = ServiceBlock(ReadRepoFile("deploy/docker/docker-compose.yml"), "auth-api");

        var index = Regex.Match(
            authApi,
            @"ServiceAuth__Clients__(\d+)__ClientId: \$\{NOTES_SERVICE_CLIENT_ID:-notes-worker\}"
        );
        Assert.True(index.Success, "auth-api must declare the notes-worker service client.");

        var permissions = Regex
            .Matches(authApi, $@"ServiceAuth__Clients__{index.Groups[1].Value}__Permissions__\d+: (\S+)")
            .Select(match => match.Groups[1].Value);
        Assert.Contains("cloudstorage.file.download", permissions);
    }

    // Bloque de un servicio: desde su línea "  nombre:" hasta el siguiente servicio del mismo nivel.
    private static string ServiceBlock(string compose, string service)
    {
        var start = Regex.Match(compose, $@"^  {Regex.Escape(service)}:\s*$", RegexOptions.Multiline);
        Assert.True(start.Success, $"Service {service} not found in docker-compose.yml.");
        var next = Regex.Match(compose[(start.Index + start.Length)..], @"^  [a-z0-9-]+:\s*$", RegexOptions.Multiline);
        return next.Success ? compose.Substring(start.Index, start.Length + next.Index) : compose[start.Index..];
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
        throw new FileNotFoundException(relativePath);
    }
}
