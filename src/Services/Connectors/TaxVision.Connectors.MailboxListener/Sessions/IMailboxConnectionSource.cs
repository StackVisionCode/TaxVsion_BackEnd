using TaxVision.Connectors.MailboxListener.Imap;

namespace TaxVision.Connectors.MailboxListener.Sessions;

public interface IMailboxConnectionSource
{
    /// <summary>Lee y descifra las credenciales justo antes de conectar. Null si la cuenta ya no las tiene.</summary>
    Task<MailboxConnectionInfo?> LoadAsync(Guid accountId, CancellationToken ct);
}
