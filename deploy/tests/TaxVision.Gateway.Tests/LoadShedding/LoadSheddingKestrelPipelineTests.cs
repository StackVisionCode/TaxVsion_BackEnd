using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// Regresión del incidente de prod con el pipeline REAL de ASP.NET sobre Kestrel: el middleware va antes
/// de <c>UseWebSockets()</c>, igual que en el Gateway. TestServer no sirve para esto — inyecta su propio
/// <c>IHttpWebSocketFeature</c> y el bug no se reproduce. Con la detección anterior, el socket de este
/// test entraba en la ventana como una muestra de cientos de milisegundos.
/// </summary>
public sealed class LoadSheddingKestrelPipelineTests
{
    private sealed class AllowAllShedder : ILoadShedder
    {
        public int RetryAfterSeconds => 5;

        public SheddingVerdict Evaluate(string tenantKey, PathString path, bool clientDisconnected) =>
            SheddingVerdict.Allowed;
    }

    private sealed class Host(WebApplication app, RequestOutcomeWindow window, Uri baseAddress, Task webSocketDone)
        : IAsyncDisposable
    {
        public RequestOutcomeWindow Window => window;
        public Uri BaseAddress => baseAddress;
        public Task WebSocketDone => webSocketDone;

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private static async Task<Host> StartAsync()
    {
        var window = new RequestOutcomeWindow(60);
        var webSocketDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        // Por fuera del load shedder: su finally ya corrió cuando esto se entera de que el socket cerró.
        app.Use(
            async (context, next) =>
            {
                await next(context);
                if (context.Request.Path.StartsWithSegments("/ws"))
                    webSocketDone.TrySetResult();
            }
        );

        // Sin prefijos pass-through: lo único que puede excluir el socket es la detección del upgrade.
        app.UseMiddleware<LoadSheddingMiddleware>(
            new AllowAllShedder(),
            window,
            new TenantConsumptionTracker(60),
            new StaticOptionsMonitor<LoadShedderOptions>(new LoadShedderOptions { PassThroughPathPrefixes = [] })
        );
        app.UseWebSockets();

        app.Map(
            "/ws",
            async context =>
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[64];
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                while (!result.CloseStatus.HasValue)
                    result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
            }
        );
        app.MapGet(
            "/streaming",
            async context =>
            {
                // Headers enseguida, cuerpo tarde: lo lento es la transferencia, no el servidor.
                await context.Response.StartAsync();
                await Task.Delay(800);
                await context.Response.WriteAsync("done");
            }
        );
        app.MapGet(
            "/slow",
            async context =>
            {
                await Task.Delay(300);
                await context.Response.WriteAsync("done");
            }
        );

        await app.StartAsync();
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.First();

        return new Host(app, window, new Uri(address), webSocketDone.Task);
    }

    [Fact]
    public async Task WebSocketDeLargaVida_AntesDeUseWebSockets_NoEntraEnLaVentana()
    {
        await using var host = await StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://{host.BaseAddress.Authority}/ws"), timeout.Token);
        await Task.Delay(400, timeout.Token);
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        await host.WebSocketDone.WaitAsync(timeout.Token);

        Assert.Equal(0, host.Window.GetSnapshot().SampleCount);
    }

    [Fact]
    public async Task RespuestaStreameada_SeMideHastaQueArrancaLaRespuesta()
    {
        await using var host = await StartAsync();
        using var http = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await http.GetStringAsync("/streaming");

        Assert.Equal("done", body);
        var snapshot = host.Window.GetSnapshot();
        Assert.Equal(1, snapshot.SampleCount);
        Assert.True(snapshot.P99LatencyMs < 800, $"Se midió la transferencia completa: p99={snapshot.P99LatencyMs}ms");
    }

    [Fact]
    public async Task RespuestaLenta_SeMideCompleta()
    {
        await using var host = await StartAsync();
        using var http = new HttpClient { BaseAddress = host.BaseAddress };

        await http.GetStringAsync("/slow");

        var snapshot = host.Window.GetSnapshot();
        Assert.Equal(1, snapshot.SampleCount);
        Assert.True(
            snapshot.P99LatencyMs >= 290,
            $"La latencia del servidor no se midió: p99={snapshot.P99LatencyMs}ms"
        );
    }
}
