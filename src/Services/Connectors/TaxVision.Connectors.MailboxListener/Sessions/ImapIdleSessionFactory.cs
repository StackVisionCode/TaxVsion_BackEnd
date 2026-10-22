using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.MailboxListener.Options;

namespace TaxVision.Connectors.MailboxListener.Sessions;

public sealed class ImapIdleSessionFactory(
    MailboxSessionDependencies deps,
    IOptions<MailboxListenerOptions> options,
    ILogger<ImapIdleSession> logger
) : IMailboxSessionFactory
{
    public IMailboxSession Create(ActiveAccountRef account) =>
        new ImapIdleSession(account, deps, options.Value, logger);
}
