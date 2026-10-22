namespace TaxVision.Connectors.MailboxListener.Imap;

/// <summary>Una conexión a la bandeja de entrada de una cuenta, solo para enterarse de que cambió.</summary>
public interface IMailboxWatcher : IAsyncDisposable
{
    /// <summary>Conecta, hace login y abre INBOX en solo lectura. Lanza <see cref="MailboxAuthenticationException"/> si rechaza la contraseña.</summary>
    Task ConnectAsync(MailboxConnectionInfo connection, CancellationToken ct);

    /// <summary>Si el servidor anuncia IDLE (push); si no, hay que preguntarle cada tanto.</summary>
    bool SupportsIdle { get; }

    /// <summary>
    /// Espera hasta <paramref name="maxWait"/> a que cambie la bandeja. Con IDLE lo avisa el servidor;
    /// sin IDLE se pregunta al terminar la espera. true si cambió.
    /// </summary>
    Task<bool> WaitForChangeAsync(TimeSpan maxWait, CancellationToken ct);
}
