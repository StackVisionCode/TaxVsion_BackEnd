using System.Text.RegularExpressions;

namespace TaxVision.Notification.Tests.Deploy;

/// <summary>
/// Cada variable de envío de campañas debe llegar de punta a punta: la consume el compose (<c>${VAR}</c>)
/// y el workflow la escribe en el .env de producción. Antes solo se escribía el secreto del webhook y las
/// campañas salían a producción sin usuario ni contraseña de SMTP2GO.
/// </summary>
public sealed class CampaignEmailDeployWiringTests
{
    public static TheoryData<string> CampaignVars =>
        new()
        {
            "CAMPAIGN_EMAIL_PROVIDER",
            "CAMPAIGN_EMAIL_SMTP_HOST",
            "CAMPAIGN_EMAIL_SMTP_PORT",
            "CAMPAIGN_EMAIL_SMTP_USE_TLS",
            "CAMPAIGN_EMAIL_SMTP_USERNAME",
            "CAMPAIGN_EMAIL_SMTP_PASSWORD",
            "CAMPAIGN_EMAIL_SMTP_FROM_ADDRESS",
            "CAMPAIGN_EMAIL_SMTP_FROM_NAME",
            "CAMPAIGN_EMAIL_API_BASE_URL",
            "CAMPAIGN_EMAIL_API_KEY",
            "CAMPAIGN_EMAIL_API_FROM_ADDRESS",
            "CAMPAIGN_EMAIL_API_FROM_NAME",
            "CAMPAIGN_EMAIL_MAX_PER_MIN",
            "CAMPAIGN_EMAIL_WEBHOOK_SECRET",
        };

    [Theory]
    [MemberData(nameof(CampaignVars))]
    public void Var_IsConsumedByCompose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Matches(new Regex(@"\$\{" + Regex.Escape(varName) + @"[:}]"), compose);
    }

    [Theory]
    [MemberData(nameof(CampaignVars))]
    public void Var_IsWrittenByDeployWorkflow(string varName)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains($"{varName}=${{{{ secrets.{varName} }}}}", workflow);
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
