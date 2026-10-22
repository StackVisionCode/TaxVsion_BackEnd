using TaxVision.Connectors.Infrastructure;
using TaxVision.Connectors.MailboxListener.Coordination;
using TaxVision.Connectors.MailboxListener.Imap;
using TaxVision.Connectors.MailboxListener.Observability;
using TaxVision.Connectors.MailboxListener.Options;
using TaxVision.Connectors.MailboxListener.Sessions;

namespace TaxVision.Connectors.MailboxListener;

public static class MailboxListenerRegistration
{
    /// <summary>
    /// Solo lo que el listener usa de Connectors: base (lectura), secretos y Redis. Nada de OAuth,
    /// proveedores ni handlers: la descarga la hace Connectors API.
    /// </summary>
    public static IServiceCollection AddMailboxListener(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddConnectorsCore(configuration);
        services.Configure<MailboxListenerOptions>(configuration.GetSection(MailboxListenerOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MailboxListenerMetrics>();
        services.AddSingleton<ConnectThrottle>();
        services.AddSingleton<IMailboxWatcherFactory, MailKitMailboxWatcherFactory>();
        services.AddSingleton<IMailboxConnectionSource, ImapCredentialsConnectionSource>();
        services.AddSingleton<IMailboxChangePublisher, WolverineMailboxChangePublisher>();
        services.AddSingleton<MailboxSessionDependencies>();
        services.AddSingleton<IMailboxSessionFactory, ImapIdleSessionFactory>();
        services.AddSingleton<MailboxListenerCoordinator>();
        services.AddHostedService<MailboxListenerService>();
        return services;
    }
}
