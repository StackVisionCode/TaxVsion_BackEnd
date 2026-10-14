using Xunit;

namespace TaxVision.Gateway.Tests.Pipeline;

/// <summary>
/// En cross-origin el navegador solo deja leer a la SPA los headers "simples"; sin exponerlos, el front
/// no ve <c>Retry-After</c> ni <c>X-RateLimit-*</c> tras un 429/503 y no puede decirle al usuario cuánto
/// esperar. La política CORS vive inline en Program.cs, así que se congela sobre el fuente (mismo
/// criterio que <see cref="GatewayWebSocketPipelineTests"/>).
/// </summary>
public sealed class GatewayCorsPolicyTests
{
    private static string ProgramSource()
    {
        return RepoFile("src/Gateway/TaxVision.Gateway/Program.cs");
    }

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Theory]
    [InlineData("Retry-After")]
    [InlineData("X-RateLimit-Limit")]
    [InlineData("X-RateLimit-Remaining")]
    [InlineData("X-RateLimit-Reset")]
    [InlineData("X-RateLimit-Policy")]
    [InlineData("X-RateLimit-Layer")]
    public void LaPoliticaSpa_ExponeLosHeadersDeRateLimit(string header)
    {
        var source = ProgramSource();
        var exposed = source.IndexOf("WithExposedHeaders(", StringComparison.Ordinal);

        Assert.True(exposed >= 0, "La política CORS \"spa\" no expone headers (falta WithExposedHeaders).");
        Assert.True(
            source.IndexOf($"\"{header}\"", exposed, StringComparison.Ordinal) >= 0,
            $"La política CORS \"spa\" no expone {header}."
        );
    }

    /// <summary>
    /// Orígenes extra (además de los de la plataforma, que salen de PlatformSettings) que aceptan el Gateway y
    /// Communication en todos los ambientes: los fronts locales para probar contra producción (CRM 4200, landing
    /// 4201/4202, portal 4300/4301, Ionic serve 8100/8101) y las apps móviles empaquetadas. Las dos listas
    /// tienen que coincidir: si una acepta un front y la otra no, la app carga pero el chat o las llamadas fallan.
    /// </summary>
    public static TheoryData<string> ExtraOrigins =>
        [
            "http://localhost:4200",
            "http://localhost:4201",
            "http://localhost:4202",
            "http://localhost:4300",
            "http://localhost:4301",
            "http://localhost:8100",
            "http://localhost:8101",
            "http://localhost",
            "https://localhost",
            "capacitor://localhost",
            "ionic://localhost",
        ];

    [Theory]
    [MemberData(nameof(ExtraOrigins))]
    public void GatewayCors_IncluyeLosOriginsExtraEnAppsettings(string origin)
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            RepoFile("src/Gateway/TaxVision.Gateway/appsettings.json")
        );
        var origins = json
            .RootElement.GetProperty("Cors")
            .GetProperty("Origins")
            .EnumerateArray()
            .Select(e => e.GetString());

        Assert.Contains(origin, origins);
    }

    /// <summary>
    /// Los orígenes de las apps de la plataforma salen del dominio vigente de PlatformSettings en cada
    /// preflight: una lista fija se quedaba con el dominio viejo al cambiarlo.
    /// </summary>
    [Fact]
    public void LaPoliticaSpa_SigueAlDominioDePlatformSettings()
    {
        var source = ProgramSource();

        Assert.Contains("SetIsOriginAllowed(", source);
        Assert.Contains("platform.Current.WebOrigins", source);
    }

    /// <summary>
    /// <see cref="PlatformSettingsSnapshot.WebOrigins"/> solo contempla los hosts fijos (apex/www/app/
    /// client). Para que CORS acepte un subdominio de oficina (<c>manfer.taxoffix.io</c>) o el panel de
    /// plataforma (<c>admin.taxoffix.io</c>) sin tocar código, el predicado delega en
    /// <c>IsPlatformHttpsOrigin</c> — la misma regla con la que <c>TenantHostGuardMiddleware</c>
    /// resuelve el host. Un olvido aquí vuelve a bloquear la SPA del tenant en cuanto se añade uno nuevo.
    /// </summary>
    [Fact]
    public void LaPoliticaSpa_AceptaSubdominiosDeOficinaYAdmin()
    {
        var source = ProgramSource();

        Assert.Contains("platform.Current.IsPlatformHttpsOrigin(", source);
    }

    [Theory]
    [MemberData(nameof(ExtraOrigins))]
    public void CommunicationCors_IncluyeOriginsExtraEnCompose(string origin)
    {
        var line = RepoFile("deploy/docker/docker-compose.yml")
            .Split('\n')
            .Single(l => l.TrimStart().StartsWith("COMMUNICATION_CORS_ORIGINS:", StringComparison.Ordinal));

        Assert.Contains(origin, line.Split(':', 2)[1].Split(',').Select(o => o.Trim()));
    }
}
