using Xunit;

namespace TaxVision.Gateway.Tests.Pipeline;

/// <summary>
/// YARP solo proxya upgrades de WebSocket si <c>UseWebSockets()</c> está en el pipeline; sin él el
/// gateway responde 400 al handshake <c>Upgrade: websocket</c> y socket.io cae a long-polling (que
/// detrás de Cloudflare se rompe por buffering ⇒ ping timeout). Probar el upgrade real exige levantar
/// Gateway + upstream + proxy, así que aquí congelamos lo que sí se puede: que el middleware esté en el
/// pipeline y ANTES de <c>MapReverseProxy()</c>, para que un refactor no lo borre y reviva el bug.
/// </summary>
public sealed class GatewayWebSocketPipelineTests
{
    private static string ProgramSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "Gateway", "TaxVision.Gateway", "Program.cs");
        return File.ReadAllText(path);
    }

    [Fact]
    public void UseWebSocketsEstaEnElPipeline()
    {
        Assert.Contains("UseWebSockets(", ProgramSource());
    }

    [Fact]
    public void UseWebSocketsVaAntesDeMapReverseProxy()
    {
        var src = ProgramSource();
        var ws = src.IndexOf("UseWebSockets(", StringComparison.Ordinal);
        var proxy = src.IndexOf("MapReverseProxy(", StringComparison.Ordinal);

        Assert.True(ws >= 0, "Falta app.UseWebSockets() en el pipeline del gateway.");
        Assert.True(proxy >= 0, "Falta app.MapReverseProxy() en el pipeline del gateway.");
        Assert.True(
            ws < proxy,
            "app.UseWebSockets() debe ir ANTES de app.MapReverseProxy() para que YARP proxye el upgrade."
        );
    }
}
