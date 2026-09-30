using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// Incidente prod (sep-2026): los upgrades WebSocket de Socket.IO son long-lived; su duración (la vida
/// del socket, minutos) entraba en la ventana del p99 y disparaba load shedding sobre toda la flota
/// (503 "Fleet is overloaded" en /campaigns, /customers, /tasks…). El primer arreglo se apoyaba solo en
/// <c>IsWebSocketRequest</c>, que antes de <c>UseWebSockets()</c> da siempre false: en el pipeline real
/// no excluía nada. Estos tests fijan la detección por header, el long-polling, las descargas ZIP, los
/// uploads grandes, la clave por IP del tráfico anónimo y el cuerpo del 503.
/// </summary>
public sealed class LoadSheddingMiddlewareTests
{
    private sealed class SpyShedder(SheddingVerdict verdict) : ILoadShedder
    {
        public int EvaluateCalls { get; private set; }
        public string? LastTenantKey { get; private set; }
        public int RetryAfterSeconds => 7;

        public SheddingVerdict Evaluate(string tenantKey, PathString path, bool clientDisconnected)
        {
            EvaluateCalls++;
            LastTenantKey = tenantKey;
            return verdict;
        }
    }

    private sealed class FakeWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<System.Net.WebSockets.WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            throw new NotSupportedException();
    }

    private sealed record Sut(
        LoadSheddingMiddleware Middleware,
        RequestOutcomeWindow Window,
        SpyShedder Shedder,
        TenantConsumptionTracker Tracker
    );

    private static Sut Create(SheddingVerdict verdict = SheddingVerdict.Allowed, LoadShedderOptions? options = null)
    {
        var window = new RequestOutcomeWindow(60);
        var shedder = new SpyShedder(verdict);
        var tracker = new TenantConsumptionTracker(60);
        var middleware = new LoadSheddingMiddleware(
            _ => Task.CompletedTask,
            shedder,
            window,
            tracker,
            new StaticOptionsMonitor<LoadShedderOptions>(options ?? new LoadShedderOptions())
        );
        return new Sut(middleware, window, shedder, tracker);
    }

    private static DefaultHttpContext BuildContext(string path = "/customers")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    [Fact]
    public async Task WebSocketUpgrade_ConLaFeatureDeUseWebSockets_NoSeCuentaNiSeSheddea()
    {
        var sut = Create();
        var context = BuildContext("/realtime");
        context.Features.Set<IHttpWebSocketFeature>(new FakeWebSocketFeature());

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal(0, sut.Shedder.EvaluateCalls);
        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task WebSocketUpgrade_AntesDeUseWebSockets_ElHeaderBastaParaExcluirlo()
    {
        // Así llega en el pipeline real: sin IHttpWebSocketFeature, solo con el header de Kestrel. Sin
        // prefijos pass-through, para que lo único que lo excluya sea el header.
        var sut = Create(options: new LoadShedderOptions { PassThroughPathPrefixes = [] });
        var context = BuildContext("/communication/socket.io/");
        context.Request.Headers.Connection = "Upgrade";
        context.Request.Headers.Upgrade = "websocket";

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal(0, sut.Shedder.EvaluateCalls);
        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Theory]
    [InlineData("/communication/socket.io/", "/communication/socket.io")]
    [InlineData("/communication/socket.io/", "communication/socket.io")] // prefijo sin "/" en config
    [InlineData("/COMMUNICATION/Socket.IO/", "/communication/socket.io")]
    public async Task SocketIoLongPolling_PorPrefijoPassThrough_NoSeCuentaNiSeSheddea(string path, string prefix)
    {
        var sut = Create(options: new LoadShedderOptions { PassThroughPathPrefixes = [prefix] });
        var context = BuildContext(path);
        context.Request.QueryString = new QueryString("?EIO=4&transport=polling");

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal(0, sut.Shedder.EvaluateCalls);
        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task PorDefecto_SocketIoEsPassThrough()
    {
        var sut = Create();

        await sut.Middleware.InvokeAsync(BuildContext("/communication/socket.io/"));

        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task RequestNormal_SeEvaluaYSeCuentaEnLaVentana()
    {
        var sut = Create();

        await sut.Middleware.InvokeAsync(BuildContext());

        Assert.Equal(1, sut.Shedder.EvaluateCalls);
        Assert.Equal(1, sut.Window.GetSnapshot().SampleCount);
    }

    [Theory]
    [InlineData("/storage/public/abc123/zip")] // "Download all" público de carpeta
    [InlineData("/storage/files/zip")] // ZIP bulk autenticado
    public async Task DescargaZip_NoSeCuentaNiSeSheddea(string path)
    {
        var sut = Create();

        await sut.Middleware.InvokeAsync(BuildContext(path));

        Assert.Equal(0, sut.Shedder.EvaluateCalls);
        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task CuerpoGrande_SeEvaluaPeroNoSeMide()
    {
        // Un upload tarda lo que tarde la subida del cliente: no es latencia del servidor.
        var sut = Create(options: new LoadShedderOptions { UnmeasuredRequestBodyBytes = 1024 });
        var context = BuildContext("/signature/documents");
        context.Request.ContentLength = 25 * 1024 * 1024;

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal(1, sut.Shedder.EvaluateCalls);
        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task SinTenantId_ElConsumoSeCuentaPorIpDelCliente()
    {
        var sut = Create();
        var context = BuildContext("/signature/public/token-1");
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal("anon:203.0.113.9", sut.Shedder.LastTenantKey);
        Assert.Equal(1, sut.Tracker.GetSnapshot("anon:203.0.113.9").TenantRequests);
    }

    [Fact]
    public async Task ConTenantId_ElConsumoSeCuentaPorTenant()
    {
        var sut = Create();
        var context = BuildContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "tenant-x")], "test"));

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal("tenant-x", sut.Shedder.LastTenantKey);
    }

    [Fact]
    public async Task Rechazo_Responde503ConRetryAfterYUnMensajeProfesional()
    {
        var sut = Create(SheddingVerdict.FairShareExcess);
        var context = BuildContext();
        context.Response.Body = new MemoryStream();

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("7", context.Response.Headers.RetryAfter.ToString());

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("LoadShedding.Active", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("retryAfterSeconds").GetInt32());

        var message = body.RootElement.GetProperty("message").GetString();
        Assert.Contains("try again in 7 seconds", message);
        Assert.DoesNotContain("Fleet", message);
        Assert.Equal(0, sut.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task ClienteDesconectado_NoEscribeRespuesta()
    {
        var sut = Create(SheddingVerdict.Abandoned);
        var context = BuildContext();
        context.Response.Body = new MemoryStream();

        await sut.Middleware.InvokeAsync(context);

        Assert.Equal(0, context.Response.Body.Length);
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact]
    public void LaMetricaAgrupaALosAnonimosSinEtiquetarLaIp()
    {
        var tenantId = Guid.NewGuid().ToString();

        Assert.Equal("anonymous", LoadSheddingMiddleware.MetricTenantKey("anon:203.0.113.7"));
        Assert.Equal(tenantId, LoadSheddingMiddleware.MetricTenantKey(tenantId));
    }
}
