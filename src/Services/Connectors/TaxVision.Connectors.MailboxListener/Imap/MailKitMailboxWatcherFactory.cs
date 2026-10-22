namespace TaxVision.Connectors.MailboxListener.Imap;

public sealed class MailKitMailboxWatcherFactory : IMailboxWatcherFactory
{
    public IMailboxWatcher Create() => new MailKitMailboxWatcher();
}
