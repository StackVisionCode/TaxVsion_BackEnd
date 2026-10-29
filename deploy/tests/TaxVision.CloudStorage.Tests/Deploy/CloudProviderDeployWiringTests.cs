using System.Text.RegularExpressions;

namespace TaxVision.CloudStorage.Tests.Deploy;

/// <summary>
/// Cada variable de Google Drive / OneDrive / Dropbox que el compose le pasa a CloudStorage tiene que
/// escribirla el workflow en el .env de producción. Si falta, el contenedor arranca con el default de
/// localhost y el connect explota en prod con redirect_uri_mismatch (o el proveedor queda apagado sin aviso).
/// </summary>
public sealed class CloudProviderDeployWiringTests
{
    [Theory]
    [InlineData("GOOGLE_DRIVE_CLIENT_ID")]
    [InlineData("GOOGLE_DRIVE_CLIENT_SECRET")]
    [InlineData("ONEDRIVE_CLIENT_ID")]
    [InlineData("ONEDRIVE_CLIENT_SECRET")]
    [InlineData("DROPBOX_CLIENT_ID")]
    [InlineData("DROPBOX_CLIENT_SECRET")]
    public void Credential_IsMappedInComposeAndWrittenByTheWorkflow(string varName)
    {
        Assert.Matches(
            new Regex(@"\$\{" + Regex.Escape(varName) + @"[:}]"),
            ReadRepoFile("deploy/docker/docker-compose.yml")
        );

        // `production` está al tope de 100 secrets: los nuevos viven en `production-extra`, que solo
        // lee el job `deploy`. Escritos en el job `stage` llegan vacíos al contenedor (503 en prod).
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");
        var line = $"{varName}=${{{{ secrets.{varName} }}}}";
        var deployJob = workflow.IndexOf("environment: production-extra", StringComparison.Ordinal);
        Assert.True(deployJob > 0, "The deploy job (environment: production-extra) was not found.");
        Assert.True(
            workflow.IndexOf(line, StringComparison.Ordinal) > deployJob,
            $"{varName} must be written by the deploy job."
        );
        Assert.Equal(1, Regex.Matches(workflow, Regex.Escape(line)).Count);
    }

    [Theory]
    [InlineData("GOOGLE_DRIVE_REDIRECT_URI", "/storage/providers/google-drive/callback")]
    [InlineData("ONEDRIVE_REDIRECT_URI", "/storage/providers/onedrive/callback")]
    [InlineData("DROPBOX_REDIRECT_URI", "/storage/providers/dropbox/callback")]
    public void Callback_IsDerivedFromTheApiHostAndMatchesTheComposeDefault(string varName, string route)
    {
        Assert.Contains(
            $"{varName}=https://${{{{ secrets.TAXVISION_DOMAIN }}}}{route}",
            ReadRepoFile(".github/workflows/deploy.yml")
        );

        var match = Regex.Match(
            ReadRepoFile("deploy/docker/docker-compose.yml"),
            @"\$\{" + Regex.Escape(varName) + @":-(?<default>[^}]+)\}"
        );
        Assert.True(match.Success, $"No default for {varName} in the compose.");
        Assert.EndsWith(route, match.Groups["default"].Value);
    }

    [Theory]
    [InlineData("GOOGLE_DRIVE_FRONTEND_RETURN_URI")]
    [InlineData("ONEDRIVE_FRONTEND_RETURN_URI")]
    [InlineData("DROPBOX_FRONTEND_RETURN_URI")]
    public void FrontendReturn_PointsToTheCrmStoragePage(string varName)
    {
        Assert.Contains(
            $"{varName}=https://app.${{{{ secrets.TAXVISION_BASE_DOMAIN }}}}/storage",
            ReadRepoFile(".github/workflows/deploy.yml")
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
