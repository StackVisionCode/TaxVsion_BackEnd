using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// Proxy TCP de caos: se pone entre la prueba y el RabbitMQ o Redis reales y corta o restaura el paso a
/// voluntad, sin tocar los contenedores (la flota local sigue funcionando). Cortado, cierra las
/// conexiones vivas con RST y cierra al instante las nuevas: para el cliente es un servidor caído.
/// </summary>
internal sealed class TcpChaosProxy : IAsyncDisposable
{
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Guid, (TcpClient Client, TcpClient Upstream)> _links = new();
    private readonly Task _acceptLoop;
    private volatile bool _open;

    public TcpChaosProxy(string targetHost, int targetPort, bool open = true)
    {
        _targetHost = targetHost;
        _targetPort = targetPort;
        _open = open;
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Corta lo que está conectado y rechaza lo nuevo.</summary>
    public void Cut()
    {
        _open = false;
        foreach (var id in _links.Keys)
            Drop(id);
    }

    public void Restore() => _open = true;

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (!_open)
            {
                Reset(client);
                continue;
            }

            _ = LinkAsync(client);
        }
    }

    private async Task LinkAsync(TcpClient client)
    {
        var upstream = new TcpClient();
        try
        {
            await upstream.ConnectAsync(_targetHost, _targetPort, _stop.Token);
        }
        catch (Exception)
        {
            Reset(client);
            upstream.Dispose();
            return;
        }

        var id = Guid.NewGuid();
        _links[id] = (client, upstream);
        // Un corte que llegó mientras se conectaba también se lleva esta conexión.
        if (!_open)
        {
            Drop(id);
            return;
        }

        try
        {
            await Task.WhenAny(PumpAsync(client, upstream), PumpAsync(upstream, client));
        }
        finally
        {
            Drop(id);
        }
    }

    private async Task PumpAsync(TcpClient from, TcpClient to)
    {
        try
        {
            await from.GetStream().CopyToAsync(to.GetStream(), _stop.Token);
        }
        catch (Exception)
        {
            // El corte cierra los dos extremos en Drop.
        }
    }

    private void Drop(Guid id)
    {
        if (!_links.TryRemove(id, out var link))
            return;
        Reset(link.Client);
        Reset(link.Upstream);
    }

    private static void Reset(TcpClient client)
    {
        try
        {
            client.Client.LingerState = new LingerOption(true, 0);
        }
        catch (Exception)
        {
            // Socket ya cerrado.
        }
        client.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        foreach (var id in _links.Keys)
            Drop(id);
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
            // Apagado.
        }
        _stop.Dispose();
    }
}
