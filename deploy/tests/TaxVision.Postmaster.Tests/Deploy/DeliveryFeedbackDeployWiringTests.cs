using System.Text.RegularExpressions;

namespace TaxVision.Postmaster.Tests.Deploy;

/// <summary>
/// La key de SMTP2GO tiene que llegar de punta a punta: el workflow la escribe en el .env y el compose
/// se la pasa a Postmaster con el nombre que lee la config. Si una punta falta, la consulta de respaldo
/// queda apagada en producción sin que nadie lo note.
/// </summary>
public sealed class DeliveryFeedbackDeployWiringTests
{
    [Fact]
    public void Smtp2GoApiKey_is_written_by_the_deploy_workflow()
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains("SMTP2GO_API_KEY=${{ secrets.SMTP2GO_API_KEY }}", workflow);
    }

    [Fact]
    public void Smtp2GoApiKey_reaches_postmaster_under_its_config_key()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // El nombre de la izquierda es el que lee Smtp2GoFeedbackOptions (DeliveryFeedback:Smtp2Go:ApiKey).
        Assert.Matches(new Regex(@"DeliveryFeedback__Smtp2Go__ApiKey:\s*\$\{SMTP2GO_API_KEY[:}]"), compose);
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

        throw new FileNotFoundException($"No se encontró '{relativePath}' subiendo desde {AppContext.BaseDirectory}.");
    }
}
