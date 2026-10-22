using TaxVision.Connectors.Application.Accounts;

namespace TaxVision.Connectors.MailboxListener.Sessions;

/// <summary>Los dos avisos que da el listener. No escribe en la base: Connectors API hace el resto.</summary>
public interface IMailboxChangePublisher
{
    Task PublishChangeAsync(ActiveAccountRef account, CancellationToken ct);

    Task PublishAuthFailedAsync(ActiveAccountRef account, CancellationToken ct);
}
