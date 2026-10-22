using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.MailboxListener.Options;

namespace TaxVision.Connectors.MailboxListener.Sessions;

/// <summary>
/// Arranque escalonado: pocos logins a la vez en total y menos aún por servidor. Al reiniciar el
/// listener, 500 cuentas no le pegan juntas al mismo servidor IMAP.
/// </summary>
public sealed class ConnectThrottle(IOptions<MailboxListenerOptions> options)
{
    private readonly SemaphoreSlim _global = new(Math.Max(1, options.Value.MaxConcurrentConnects));
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perHost = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ejecuta el login respetando los dos topes y deja una pausa antes de liberar el lugar.</summary>
    public async Task RunAsync(string host, Func<Task> connect, CancellationToken ct)
    {
        var hostGate = _perHost.GetOrAdd(
            host,
            _ => new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentConnectsPerHost))
        );
        await _global.WaitAsync(ct);
        try
        {
            await hostGate.WaitAsync(ct);
            try
            {
                await connect();
                await Task.Delay(Math.Max(0, options.Value.ConnectDelayMilliseconds), ct);
            }
            finally
            {
                hostGate.Release();
            }
        }
        finally
        {
            _global.Release();
        }
    }
}
