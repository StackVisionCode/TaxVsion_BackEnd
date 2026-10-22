using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.MailboxListener.Imap;
using TaxVision.Connectors.MailboxListener.Observability;

namespace TaxVision.Connectors.MailboxListener.Sessions;

/// <summary>Lo que comparten todas las sesiones de una instancia (singletons).</summary>
public sealed record MailboxSessionDependencies(
    IMailboxConnectionSource ConnectionSource,
    IMailboxWatcherFactory WatcherFactory,
    ConnectThrottle Throttle,
    IMailboxChangePublisher Publisher,
    IMailboxListenerPresence Presence,
    MailboxListenerMetrics Metrics,
    TimeProvider Time
);
