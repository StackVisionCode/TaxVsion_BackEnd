using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// Incidente prod (sep-2026): los upgrades WebSocket de Socket.IO son long-lived; su duración (la vida
/// del socket, minutos) entraba en la ventana del p99 y disparaba load shedding sobre toda la flota
/// (503 en /campaigns, /customers, /tasks…). El fix los excluye igual que /health: ni se cuentan ni se
/// sheddean. La misma clase de bug aplica a las descargas ZIP streameadas (/storage/**/zip): su
/// duración es tiempo de descarga del cliente, no carga del servidor. Estos tests fijan ambas exclusiones.
/// </summary>
public sealed class LoadSheddingMiddlewareTests
{
    private sealed class SpyShedder : ILoadShedder
    {
        public int EvaluateCalls { get; private set; }
        public int RetryAfterSeconds => 1;

        public SheddingVerdict Evaluate(string tenantKey, PathString path, bool clientDisconnected)
        {
            EvaluateCalls++;
            return SheddingVerdict.Allowed;
        }
    }

    private sealed class FakeWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<System.Net.WebSockets.WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            throw new NotSupportedException();
    }

    private static HttpContext BuildContext(bool webSocket, string path = "/communication/socket.io/")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (webSocket)
            context.Features.Set<IHttpWebSocketFeature>(new FakeWebSocketFeature());
        return context;
    }

    [Fact]
    public async Task WebSocketUpgrade_NoSeCuentaEnLaVentana_NiSeConsultaAlShedder()
    {
        var window = new RequestOutcomeWindow(60);
        var shedder = new SpyShedder();
        var middleware = new LoadSheddingMiddleware(
            _ => Task.CompletedTask,
            shedder,
            window,
            new TenantConsumptionTracker(60)
        );

        await middleware.InvokeAsync(BuildContext(webSocket: true));

        Assert.Equal(0, shedder.EvaluateCalls);
        Assert.Equal(0, window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task RequestNormal_SeCuentaEnLaVentana()
    {
        var window = new RequestOutcomeWindow(60);
        var shedder = new SpyShedder();
        var middleware = new LoadSheddingMiddleware(
            _ => Task.CompletedTask,
            shedder,
            window,
            new TenantConsumptionTracker(60)
        );

        await middleware.InvokeAsync(BuildContext(webSocket: false));

        Assert.Equal(1, shedder.EvaluateCalls);
        Assert.Equal(1, window.GetSnapshot().SampleCount);
    }

    [Theory]
    [InlineData("/storage/public/abc123/zip")] // "Download all" público de carpeta (8.2)
    [InlineData("/storage/files/zip")] // ZIP bulk autenticado
    public async Task DescargaZip_NoSeCuentaEnLaVentana_NiSeConsultaAlShedder(string path)
    {
        var window = new RequestOutcomeWindow(60);
        var shedder = new SpyShedder();
        var middleware = new LoadSheddingMiddleware(
            _ => Task.CompletedTask,
            shedder,
            window,
            new TenantConsumptionTracker(60)
        );

        await middleware.InvokeAsync(BuildContext(webSocket: false, path));

        Assert.Equal(0, shedder.EvaluateCalls);
        Assert.Equal(0, window.GetSnapshot().SampleCount);
    }
}
