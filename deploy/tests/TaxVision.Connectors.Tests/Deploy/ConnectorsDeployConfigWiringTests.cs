using System.Text.RegularExpressions;

namespace TaxVision.Connectors.Tests.Deploy;

/// <summary>
/// Fitness function del deploy: cada secreto OAuth/webhook de Connectors debe estar cableado de punta
/// a punta — mapeado en docker-compose.yml (<c>${VAR}</c>) Y escrito en el .env de producción por el
/// workflow (<c>VAR=${{ secrets.VAR }}</c>). Un secreto en el compose pero no en el action arranca el
/// contenedor con el default de localhost y el connect explota en prod con redirect_uri_mismatch;
/// este test lo caza en CI antes del deploy. Además fija que los defaults de redirect del compose
/// coincidan byte a byte con las rutas que los controllers realmente sirven (la desincronización que
/// ya nos mordió: <c>/oauth/google/callback</c> vs <c>/oauth/callback/gmail</c>).
/// </summary>
public sealed class ConnectorsDeployConfigWiringTests
{
    // Los que DEBEN venir por entorno (secreto/URL por dominio) — no los que tienen default sano y
    // fijo (p.ej. Reconciliation:IntervalMinutes), que a propósito no se exponen como secreto.
    private static readonly string[] RequiredPerEnvVars =
    [
        "CONNECTORS_GOOGLE_REDIRECT_URI",
        "CONNECTORS_MICROSOFT_REDIRECT_URI",
        "CONNECTORS_MICROSOFT_ADMIN_CONSENT_REDIRECT_URI",
        "CONNECTORS_GMAIL_WATCH_TOPIC",
        "CONNECTORS_GRAPH_NOTIFICATION_URL",
        "CONNECTORS_GRAPH_WATCH_CLIENT_STATE",
        "CONNECTORS_GMAIL_PUSH_AUDIENCE",
    ];

    [Theory]
    [MemberData(nameof(RequiredVars))]
    public void PerEnvVar_IsMappedInCompose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // Debe consumirse como ${VAR...} en el compose (con o sin :-default).
        Assert.Matches(new Regex(@"\$\{" + Regex.Escape(varName) + @"[:}]"), compose);
    }

    [Theory]
    [MemberData(nameof(RequiredVars))]
    public void PerEnvVar_IsPassedByDeployWorkflow(string varName)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        // El .env de prod se escribe como VAR=${{ secrets.VAR }} — sin esta línea el contenedor cae al default.
        Assert.Contains($"{varName}=${{{{ secrets.{varName} }}}}", workflow);
    }

    [Theory]
    [InlineData("CONNECTORS_GOOGLE_REDIRECT_URI", "/connectors/oauth/callback/gmail")]
    [InlineData("CONNECTORS_MICROSOFT_REDIRECT_URI", "/connectors/oauth/callback/graph")]
    [InlineData("CONNECTORS_MICROSOFT_ADMIN_CONSENT_REDIRECT_URI", "/connectors/oauth/admin-consent-callback")]
    public void ComposeDefault_MatchesControllerRoute(string varName, string expectedPathSuffix)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // Formato: KEY: ${VAR:-http://localhost:5047<suffix>} — capturamos el default tras ":-".
        var match = Regex.Match(compose, @"\$\{" + Regex.Escape(varName) + @":-(?<default>[^}]+)\}");
        Assert.True(match.Success, $"No se encontró un default para {varName} en el compose.");
        Assert.EndsWith(expectedPathSuffix, match.Groups["default"].Value);
    }

    public static IEnumerable<object[]> RequiredVars() => RequiredPerEnvVars.Select(v => new object[] { v });

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
            $"No se pudo localizar '{relativePath}' subiendo desde {AppContext.BaseDirectory} — ¿cambió el layout del repo?"
        );
    }
}
