using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// `production` llegó al tope de 100 secrets de GitHub, así que los nuevos viven en `production-extra`.
/// Ese environment solo lo lee el job `deploy`: si la línea <c>VAR=${{ secrets.VAR }}</c> queda en el
/// job `stage`, el .env sale con la variable vacía y el contenedor arranca sin ella (pasó con Google
/// Drive: 503 "not configured" en producción con el secret cargado en GitHub).
/// </summary>
public sealed class ProductionExtraSecretsDeployTests
{
    [Theory]
    [InlineData("GOOGLE_DRIVE_CLIENT_ID")]
    [InlineData("GOOGLE_DRIVE_CLIENT_SECRET")]
    [InlineData("ONEDRIVE_CLIENT_ID")]
    [InlineData("ONEDRIVE_CLIENT_SECRET")]
    [InlineData("DROPBOX_CLIENT_ID")]
    [InlineData("DROPBOX_CLIENT_SECRET")]
    [InlineData("CAMPAIGN_EMAIL_PROVIDER")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_HOST")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_PORT")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_USE_TLS")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_USERNAME")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_PASSWORD")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_FROM_ADDRESS")]
    [InlineData("CAMPAIGN_EMAIL_SMTP_FROM_NAME")]
    [InlineData("CAMPAIGN_EMAIL_API_BASE_URL")]
    [InlineData("CAMPAIGN_EMAIL_API_KEY")]
    [InlineData("CAMPAIGN_EMAIL_API_FROM_ADDRESS")]
    [InlineData("CAMPAIGN_EMAIL_API_FROM_NAME")]
    [InlineData("CAMPAIGN_EMAIL_MAX_PER_MIN")]
    [InlineData("CAMPAIGN_EMAIL_WEBHOOK_SECRET")]
    [InlineData("SMTP2GO_API_KEY")]
    [InlineData("CONNECTORS_LISTENER_ENABLED")]
    [InlineData("TASKS_CLIENT_FORMS_TURNSTILE_SITE_KEY")]
    [InlineData("TASKS_CLIENT_FORMS_TURNSTILE_SECRET_KEY")]
    public void Secret_kept_in_production_extra_is_written_by_the_deploy_job(string variable)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");
        var line = $"{variable}=${{{{ secrets.{variable} }}}}";
        var deployJob = workflow.IndexOf("environment: production-extra", StringComparison.Ordinal);

        Assert.True(deployJob > 0, "The deploy job (environment: production-extra) was not found.");
        Assert.Single(Regex.Matches(workflow, Regex.Escape(line)));
        Assert.True(
            workflow.IndexOf(line, StringComparison.Ordinal) > deployJob,
            $"{variable} lives in production-extra: only the deploy job can read it."
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
