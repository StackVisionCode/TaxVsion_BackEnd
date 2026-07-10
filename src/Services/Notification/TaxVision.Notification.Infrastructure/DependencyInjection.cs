using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Email.Accounts;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Infrastructure.Email;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using TaxVision.Notification.Infrastructure.Providers;
using TaxVision.Notification.Infrastructure.Sms;
using TaxVision.Notification.Infrastructure.Storage;
using TaxVision.Notification.Infrastructure.Templates;

namespace TaxVision.Notification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<NotificationDbContext>(options => options.UseSqlServer(connectionString));

        services.Configure<PortalOptions>(configuration.GetSection(PortalOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NotificationDbContext>());
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ISmsSender, LoggingSmsSender>();
        services.AddScoped<NotificationDispatcher>();

        // Cifrado compartido de secretos (Encryption:MasterKey) para configuraciones y tokens.
        services.AddSecretProtection();

        // Módulo de configuración SMTP/API (proveedores de envío).
        services.AddScoped<IEmailProviderConfigurationRepository, EmailProviderConfigurationRepository>();
        services.AddScoped<IEmailConfigurationResolver, EmailConfigurationResolver>();
        services.AddScoped<ISmtpSendClient, SystemNetSmtpSendClient>();

        // Módulo de plantillas y layouts (metadata en BD; contenido en CloudStorage).
        services.Configure<CloudStorageClientOptions>(configuration.GetSection(CloudStorageClientOptions.SectionName));
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IEmailLayoutRepository, EmailLayoutRepository>();
        services.AddSingleton<ITemplateRenderer, FluidTemplateRenderer>();
        services.AddScoped<ITemplateStorageService, TemplateStorageService>();
        services.AddScoped<ILayoutStorageService, LayoutStorageService>();

        // Módulo de envío (correos salientes, entrega asíncrona).
        services.AddScoped<IOutboundEmailRepository, OutboundEmailRepository>();
        services.AddScoped<IEmailDeliveryService, EmailDeliveryService>();

        // Módulo de campañas.
        services.AddScoped<IEmailCampaignRepository, EmailCampaignRepository>();

        // Módulo de cuentas + sincronización (proveedores externos).
        services.Configure<EmailOAuthOptions>(configuration.GetSection(EmailOAuthOptions.SectionName));
        services.AddHttpClient();
        services.AddScoped<OAuthTokenService>();
        services.AddScoped<IEmailAccountRepository, EmailAccountRepository>();
        services.AddScoped<IEmailSyncService, EmailSyncService>();
        services.AddScoped<IEmailProviderAdapter, ImapEmailProviderAdapter>();
        services.AddScoped<IEmailProviderAdapter, GmailApiProviderAdapter>();
        services.AddScoped<IEmailProviderAdapter, MicrosoftGraphProviderAdapter>();
        services.AddScoped<IEmailProviderAdapterFactory, EmailProviderAdapterFactory>();

        return services;
    }
}
