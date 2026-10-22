using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.MailboxListener.Imap;
using TaxVision.Connectors.MailboxListener.Observability;
using TaxVision.Connectors.MailboxListener.Options;

namespace TaxVision.Connectors.MailboxListener.Sessions;

/// <summary>
/// Escucha una cuenta IMAP hasta que la cancelen. Solo avisa: la descarga la hace Connectors API al
/// recibir el aviso. Si la conexión se cae, reconecta con espera creciente; si la contraseña es
/// rechazada, avisa y termina.
/// </summary>
public sealed class ImapIdleSession(
    ActiveAccountRef account,
    MailboxSessionDependencies deps,
    MailboxListenerOptions options,
    ILogger logger
) : IMailboxSession
{
    /// <summary>La marca de "escuchando" vence sola si el listener muere sin limpiar.</summary>
    public static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan PresenceRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(5);

    private int _consecutiveFailures;

    public Guid AccountId => account.Id;

    public async Task RunAsync(CancellationToken ct)
    {
        deps.Metrics.SessionStarted();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Solo vuelve sin excepción cuando no hay que seguir (sin credenciales o login rechazado).
                    await ListenAsync(ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    deps.Metrics.Reconnects.Add(1);
                    logger.LogWarning(
                        "Mailbox listener lost account {AccountId} ({Failures} in a row): {Error}",
                        account.Id,
                        _consecutiveFailures,
                        ex.GetType().Name
                    );
                    if (!await BackoffAsync(ct))
                        return;
                }
            }
        }
        finally
        {
            deps.Metrics.SessionStopped();
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        await using var watcher = deps.WatcherFactory.Create();
        if (!await ConnectAsync(watcher, ct))
            return;

        _consecutiveFailures = 0;
        var idle = watcher.SupportsIdle;
        var window = TimeSpan.FromMinutes(idle ? options.IdleMinutes : options.PollMinutes);
        deps.Metrics.Listening(idle);
        try
        {
            // Lo que llegó mientras no se escuchaba (arranque o caída) se recupera con un aviso inicial.
            await PublishChangeAsync(ct);
            var markedAt = DateTimeOffset.MinValue;
            while (true)
            {
                var now = deps.Time.GetUtcNow();
                if (now - markedAt >= PresenceRefresh)
                {
                    await MarkListeningAsync(ct);
                    markedAt = now;
                }

                if (!await watcher.WaitForChangeAsync(window, ct))
                    continue;

                // Una ráfaga de correos se junta en un solo aviso.
                await Task.Delay(TimeSpan.FromSeconds(options.ChangeDebounceSeconds), deps.Time, ct);
                await PublishChangeAsync(ct);
            }
        }
        finally
        {
            deps.Metrics.StoppedListening(idle);
        }
    }

    /// <summary>false = no hay que seguir: sin credenciales o contraseña rechazada.</summary>
    private async Task<bool> ConnectAsync(IMailboxWatcher watcher, CancellationToken ct)
    {
        // Se descifra justo antes del login y la contraseña no queda en ningún campo.
        var connection = await deps.ConnectionSource.LoadAsync(account.Id, ct);
        if (connection is null)
        {
            logger.LogInformation("Mailbox listener skips account {AccountId}: no IMAP credentials", account.Id);
            return false;
        }

        try
        {
            await deps.Throttle.RunAsync(connection.Host, () => watcher.ConnectAsync(connection, ct), ct);
            return true;
        }
        catch (MailboxAuthenticationException)
        {
            deps.Metrics.AuthFailures.Add(1);
            logger.LogWarning("Mailbox listener: IMAP login rejected for account {AccountId}", account.Id);
            await deps.Publisher.PublishAuthFailedAsync(account, ct);
            return false;
        }
    }

    /// <summary>Si Redis falla no se corta el IMAP: sin la marca, la reconciliación solo vuelve a su ritmo normal.</summary>
    private async Task MarkListeningAsync(CancellationToken ct)
    {
        try
        {
            await deps.Presence.MarkListeningAsync(account.Id, PresenceTtl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "Mailbox listener could not mark account {AccountId} as listened: {Error}",
                account.Id,
                ex.GetType().Name
            );
        }
    }

    private async Task PublishChangeAsync(CancellationToken ct)
    {
        deps.Metrics.ChangesDetected.Add(1);
        await deps.Publisher.PublishChangeAsync(account, ct);
    }

    private async Task<bool> BackoffAsync(CancellationToken ct)
    {
        deps.Metrics.BackoffStarted();
        try
        {
            await Task.Delay(BackoffFor(_consecutiveFailures, options.MaxBackoffMinutes), deps.Time, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            deps.Metrics.BackoffEnded();
        }
    }

    /// <summary>5 s, 10 s, 20 s… hasta el tope, con ±20% para que las cuentas no reconecten juntas.</summary>
    public static TimeSpan BackoffFor(int failures, int maxBackoffMinutes)
    {
        var exponent = Math.Clamp(failures - 1, 0, 16);
        var seconds = Math.Min(FirstBackoff.TotalSeconds * Math.Pow(2, exponent), maxBackoffMinutes * 60.0);
        var jitter = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromSeconds(seconds * jitter);
    }
}
