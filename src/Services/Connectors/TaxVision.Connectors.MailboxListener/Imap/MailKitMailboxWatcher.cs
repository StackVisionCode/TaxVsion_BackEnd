using MailKit;
using MailKit.Security;
using MailKitImapClient = MailKit.Net.Imap.ImapClient;

namespace TaxVision.Connectors.MailboxListener.Imap;

/// <summary>
/// Una conexión MailKit por cuenta. Sin ProtocolLogger: el protocolo lleva el login. El cambio lo avisa
/// <c>CountChanged</c> (EXISTS/EXPUNGE), que también llega con NOOP en servidores sin IDLE.
/// </summary>
public sealed class MailKitMailboxWatcher : IMailboxWatcher
{
    private readonly MailKitImapClient _client = new();
    private IMailFolder? _inbox;
    private CancellationTokenSource? _idleDone;
    private bool _changed;

    public bool SupportsIdle => _client.Capabilities.HasFlag(MailKit.Net.Imap.ImapCapabilities.Idle);

    public async Task ConnectAsync(MailboxConnectionInfo connection, CancellationToken ct)
    {
        var socket = connection.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await _client.ConnectAsync(connection.Host, connection.Port, socket, ct);
        try
        {
            await _client.AuthenticateAsync(connection.Username, connection.Password, ct);
        }
        catch (AuthenticationException ex)
        {
            throw new MailboxAuthenticationException(ex);
        }

        _inbox = _client.Inbox;
        await _inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        _inbox.CountChanged += OnCountChanged;
    }

    public async Task<bool> WaitForChangeAsync(TimeSpan maxWait, CancellationToken ct)
    {
        _changed = false;
        if (SupportsIdle)
        {
            using var done = new CancellationTokenSource(maxWait);
            _idleDone = done;
            try
            {
                await _client.IdleAsync(done.Token, ct);
            }
            finally
            {
                _idleDone = null;
            }
        }
        else
        {
            await Task.Delay(maxWait, ct);
            // NOOP hace que el servidor informe los EXISTS pendientes y dispara CountChanged.
            await _client.NoOpAsync(ct);
        }
        return _changed;
    }

    private void OnCountChanged(object? sender, EventArgs e)
    {
        _changed = true;
        // Sale del IDLE ya: la descarga la hace Connectors, acá solo se avisa.
        _idleDone?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (_inbox is not null)
            _inbox.CountChanged -= OnCountChanged;
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(quit: true);
        }
        catch (Exception)
        {
            // Cerrar una conexión ya rota no es un error que importe.
        }
        _client.Dispose();
    }
}
