using TaxVision.Connectors.Application.Accounts;

namespace TaxVision.Connectors.MailboxListener.Sessions;

public interface IMailboxSessionFactory
{
    IMailboxSession Create(ActiveAccountRef account);
}
