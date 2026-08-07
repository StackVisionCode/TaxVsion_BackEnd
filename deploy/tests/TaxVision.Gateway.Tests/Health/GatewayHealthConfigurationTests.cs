using System.Text.Json;
using Xunit;

namespace TaxVision.Gateway.Tests.Health;

/// <summary>
/// GW-06 y GW-07. El comportamiento real (un upstream caído ⇒ <c>Degraded</c>, no <c>Unhealthy</c>)
/// exige levantar Gateway + upstream y no se puede afirmar sin entorno; lo que sí se puede congelar
/// aquí es la <b>configuración</b>, que es justo donde vivían los dos modos de fallo.
/// </summary>
public sealed class GatewayHealthConfigurationTests
{
    private static JsonElement Settings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "Gateway", "TaxVision.Gateway", "appsettings.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static IEnumerable<(string Name, JsonElement Cluster)> Clusters() =>
        Settings().GetProperty("ReverseProxy").GetProperty("Clusters").EnumerateObject().Select(c => (c.Name, c.Value));

    [Fact]
    public void TodoClusterTieneActiveHealthCheckHabilitado()
    {
        var missing = Clusters()
            .Where(c =>
                !c.Cluster.TryGetProperty("HealthCheck", out var hc)
                || !hc.TryGetProperty("Active", out var active)
                || !active.GetProperty("Enabled").GetBoolean()
            )
            .Select(c => c.Name)
            .ToArray();

        Assert.True(missing.Length == 0, $"Clusters sin ActiveHealthCheck: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ElActiveHealthCheckApuntaALiveness_NoAReadiness()
    {
        // Apuntar a /health/ready haria del health check un single point of failure: el ready de un
        // upstream depende de SU base de datos, y un fallo ahi sacaria al destino del enrutado.
        var wrong = Clusters()
            .Where(c =>
                c.Cluster.GetProperty("HealthCheck").GetProperty("Active").GetProperty("Path").GetString()
                != "/health/live"
            )
            .Select(c => c.Name)
            .ToArray();

        Assert.True(wrong.Length == 0, $"Clusters que no sondean /health/live: {string.Join(", ", wrong)}");
    }

    [Fact]
    public void PassiveHealthCheckEstaDesactivado()
    {
        // Passive + 1 destino por cluster es la combinacion mas peligrosa: un pico de 5xx corta todo
        // el trafico hasta el ReactivationPeriod, amplificando el incidente en vez de contenerlo.
        var enabled = Clusters()
            .Where(c =>
                c.Cluster.GetProperty("HealthCheck").TryGetProperty("Passive", out var passive)
                && passive.GetProperty("Enabled").GetBoolean()
            )
            .Select(c => c.Name)
            .ToArray();

        Assert.True(enabled.Length == 0, $"Clusters con Passive health check activo: {string.Join(", ", enabled)}");
    }

    [Fact]
    public void LaPoliticaDeDestinosEsHealthyOrPanic()
    {
        // Con un solo destino por cluster, HealthyAndUnknown convierte el health check en un single
        // point of failure. HealthyOrPanic mantiene el enrutado y deja el check como observabilidad.
        var wrong = Clusters()
            .Where(c =>
                !c.Cluster.TryGetProperty("AvailableDestinationsPolicy", out var policy)
                || policy.GetString() != "HealthyOrPanic"
            )
            .Select(c => c.Name)
            .ToArray();

        Assert.True(wrong.Length == 0, $"Clusters sin HealthyOrPanic: {string.Join(", ", wrong)}");
    }
}
