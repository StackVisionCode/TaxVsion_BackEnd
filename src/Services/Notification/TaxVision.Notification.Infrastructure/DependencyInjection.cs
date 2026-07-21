using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Infrastructure.Email;
using TaxVision.Notification.Infrastructure.Permissions;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using TaxVision.Notification.Infrastructure.Push;
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

        // Fase 4 del plan de notificaciones dinámicas — proyecciones locales de permisos
        // (alimentadas por UserRolesChanged/RolePermissionsChanged de Auth) + el resolver
        // que las usa para audiencias ByPermission.
        services.AddScoped<IUserPermissionsProjectionRepository, UserPermissionsProjectionRepository>();
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();
        services.AddScoped<IRecipientResolver, RecipientResolver>();

        // Fase 5 — el interruptor que consulta NotificationDispatcher antes de cada envío.
        services.AddScoped<IUserNotificationPreferenceRepository, UserNotificationPreferenceRepository>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ISmsSender, LoggingSmsSender>();

        // Fase 7 del plan de notificaciones dinámicas — Notification:UseFcmPush sigue el mismo
        // idiom que Notification:UsePostmasterDispatch (flag explícito, default false hasta
        // tener credenciales reales de Firebase configuradas) en vez del patrón "presence-gated"
        // que usa CmsSignerOptions en Signature para su PFX: acá se prefiere el flag explícito
        // porque el path del JSON de service account puede estar seteado en algunos ambientes
        // (staging) sin querer activar FCM todavía. GetValue<bool> con la clave ausente resuelve
        // a false — coincide con el default seguro (sin credenciales, no intentar inicializar
        // Firebase y arrancar con LoggingPushSender, igual que antes de esta fase).
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        var useFcmPush = configuration.GetValue<bool>("Notification:UseFcmPush");
        if (useFcmPush)
        {
            services.AddScoped<IPushSender, FcmPushSender>();
        }
        else
        {
            services.AddScoped<IPushSender, LoggingPushSender>();
        }
        services.AddScoped<IPushDeviceTokenRepository, PushDeviceTokenRepository>();
        services.AddScoped<NotificationDispatcher>();

        // Notifications Fase 3+4, flippeado a default-true en Hardening Fase 21 (2026-07-18).
        //
        // - Notification:UsePostmasterDispatch = true (DEFAULT desde Fase 21): publica
        //   notifications.email_send_requested.v1 hacia Postmaster; los callbacks
        //   PostmasterEmailDelivery* actualizan el estado del NotificationDispatchAttempt.
        // - Notification:UsePostmasterDispatch = false (rollback explícito, ver README §28/§35):
        //   gateway in-process, envío via SmtpEmailSender directo. Comportamiento pre-Fase 3
        //   preservado como fallback — InProcessEmailDispatchGateway NO se elimina en esta fase
        //   (retiro real es trabajo futuro fuera de este plan, condicionado a confianza
        //   operacional en un despliegue real).
        //
        // GetValue<bool> con la clave ausente resuelve a default(bool) = false — por eso el
        // default real está fijado explícitamente en appsettings.json ("Notification:
        // UsePostmasterDispatch": true) y en el fallback de docker-compose.yml
        // (${NOTIFICATION_USE_POSTMASTER_DISPATCH:-true}), no solo acá.
        var usePostmasterDispatch = configuration.GetValue<bool>("Notification:UsePostmasterDispatch");
        if (usePostmasterDispatch)
        {
            services.AddScoped<IEmailDispatchGateway, EventBasedEmailDispatchGateway>();
        }
        else
        {
            services.AddScoped<IEmailDispatchGateway, InProcessEmailDispatchGateway>();
        }

        // Hardening Fase 19 (2026-07-18) — mismo flag, segundo punto de invocación: EmailDeliveryService
        // es el transporte real detrás de POST /notifications/email/send y de EmailCampaigns (que crea
        // OutboundEmailMessage + publica EmailSendRequestedIntegrationEvent exactamente igual que el
        // envío individual — investigado, ver el comentario de clase de PostmasterEmailDeliveryService
        // para el porqué es seguro alcanzarlo con este cambio). Se reusa Notification:UsePostmasterDispatch
        // en vez de un flag propio porque ambos interruptores responden la MISMA pregunta operacional
        // ("¿Postmaster ya es el único transporte de salida de Notification?"), no dos preguntas
        // independientes — tenerlos separados solo crearía combinaciones a medio migrar sin ningún
        // beneficio real (nadie querría el gateway de Auth/Signature/Communication en Postmaster con
        // EmailDeliveryService todavía en SMTP directo, o viceversa).
        //
        // - true (DEFAULT desde Fase 21): PostmasterEmailDeliveryService — publica
        //   notifications.email_send_requested.v1; los callbacks los resuelve
        //   PostmasterOutboundEmailCallbackConsumers (Consumers/Postmaster/), NO el mismo
        //   PostmasterCallbackConsumers.cs del gateway de arriba (ese resuelve contra
        //   NotificationLog; este resuelve contra OutboundEmailMessage — ver comentario de clase).
        // - false (rollback explícito): EmailDeliveryService (esta clase, sin cambios) — resuelve
        //   EmailProviderConfiguration propia y envía via ISmtpSendClient/SystemNetSmtpSendClient.
        if (usePostmasterDispatch)
        {
            services.AddScoped<IEmailDeliveryService, PostmasterEmailDeliveryService>();
        }
        else
        {
            services.AddScoped<IEmailDeliveryService, EmailDeliveryService>();
        }
        services.AddScoped<INotificationLogQueryRepository, NotificationLogQueryRepository>();
        services.AddScoped<IIntegrationEventPublisher, Messaging.WolverineIntegrationEventPublisher>();

        // Cifrado compartido de secretos (Encryption:MasterKey) para configuraciones y tokens.
        services.AddSecretProtection();

        // Módulo de configuración SMTP/API (proveedores de envío). NO retirado en la Fase 21 del plan
        // de hardening (Notification, 2026-07-18) aunque el default ya es Postmaster: mientras el flag
        // siga existiendo como rollback (setear Notification:UsePostmasterDispatch=false vuelve a este
        // path), EmailProviderConfigurationRepository/EmailConfigurationResolver/SystemNetSmtpSendClient
        // tienen que seguir registrados y funcionales. También los sigue usando TestEmailConfiguration
        // (POST /notifications/email/configurations/{id}/test), que no pasa por EmailDeliveryService en
        // absoluto ni por el flag. Retiro completo condicionado a una fase futura fuera de este plan,
        // cuando haya confianza operacional real para eliminar InProcessEmailDispatchGateway/
        // EmailDeliveryService y el flag mismo (ver plan, sección Fase 21, "Qué hacer").
        // SmtpEmailSender (IEmailSender, distinto de ISmtpSendClient) NO es parte de esta cadena — lo
        // usa InProcessEmailDispatchGateway (el OTRO path, Auth/Signature/Communication) vía SmtpOptions
        // global, nada que ver con EmailProviderConfiguration por tenant; el texto original de la Fase 19
        // lo listaba junto a SystemNetSmtpSendClient para retirar, pero son dos implementaciones de dos
        // interfaces distintas para dos paths distintos — corregido tras verificar el código real.
        services.AddScoped<IEmailProviderConfigurationRepository, EmailProviderConfigurationRepository>();
        services.AddScoped<IEmailConfigurationResolver, EmailConfigurationResolver>();
        services.AddScoped<ISmtpSendClient, SystemNetSmtpSendClient>();

        // Módulo de plantillas y layouts (metadata en BD; contenido en CloudStorage).
        // NO retirado en la Fase 18 del plan de hardening (Notification): el self-service HTTP
        // de un envío ad-hoc por plantilla (POST /notifications/email/send-template) estaba
        // confirmado sin caller real y se eliminó, pero este módulo entero (repos, renderer,
        // storage services) sigue siendo una dependencia real y viva de EmailCampaigns
        // (EmailCampaignBatchConsumer/ScheduleEmailCampaignHandler/SendCampaignTestHandler,
        // fuera de alcance de este plan por instrucción explícita del usuario) — ver el
        // comentario XML de EmailTemplatesController/EmailLayoutsController para el detalle
        // completo de por qué esos dos controllers tampoco se pudieron retirar.
        services.Configure<CloudStorageClientOptions>(configuration.GetSection(CloudStorageClientOptions.SectionName));
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IEmailLayoutRepository, EmailLayoutRepository>();
        services.AddSingleton<ITemplateRenderer, FluidTemplateRenderer>();
        services.AddScoped<ITemplateStorageService, TemplateStorageService>();
        services.AddScoped<ILayoutStorageService, LayoutStorageService>();

        // Módulo de envío (correos salientes, entrega asíncrona). IEmailDeliveryService se registra
        // más arriba, gateado por Notification:UsePostmasterDispatch (Fase 19) — no acá.
        services.AddScoped<IOutboundEmailRepository, OutboundEmailRepository>();

        // Módulo de campañas.
        services.AddScoped<IEmailCampaignRepository, EmailCampaignRepository>();

        return services;
    }
}
