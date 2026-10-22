namespace TaxVision.Connectors.MailboxListener.Sessions;

/// <summary>Escucha una cuenta hasta que la cancelen o hasta que no tenga sentido seguir.</summary>
public interface IMailboxSession
{
    Task RunAsync(CancellationToken ct);
}
