using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Authorization.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Application.RateLimiting.Abstractions;
using TaxVision.Notification.Infrastructure.Email;
using TaxVision.Notification.Infrastructure.Permissions;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using TaxVision.Notification.Infrastructure.Push;
using TaxVision.Notification.Infrastructure.RateLimiting;
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
        services.AddScoped<
            INotificationRecipientPermissionsProjectionRepository,
            NotificationRecipientPermissionsProjectionRepository
        >();
        services.AddScoped<
            INotificationRecipientRolePermissionsProjectionRepository,
            NotificationRecipientRolePermissionsProjectionRepository
        >();
        services.AddScoped<IRecipientResolver, RecipientResolver>();

        // PayFlow (Fase 12) — resuelve la carrera OnboardingRegistrationReady/OnboardingReceiptReady
        // (ver OnboardingReceiptLookup). El cliente M2M al endpoint one-shot de tokens de Auth se
        // registra en Program.cs (necesita HttpClient con BaseAddress, igual que Scribe/CloudStorage).
        services.AddScoped<IOnboardingReceiptLookupRepository, OnboardingReceiptLookupRepository>();

        // RBAC Fase 7 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos para AUTORIZACION,
        // consultada por ProjectionPermissionsSource cuando Authorization:PermissionsSource=
        // "Projection". Distinta de la proyeccion de arriba (Fase 4, fan-out de notificaciones,
        // NotificationRecipientPermissionsProjection) — ver el comentario XML de
        // AuthzUserPermissionsProjection. La misma instancia scoped
        // satisface el puerto local rico (para los consumers) y el puerto compartido y angosto
        // de BuildingBlocks (para la autorizacion), evitando dos lecturas separadas del mismo dato.
        services.AddScoped<AuthzUserPermissionsProjectionRepository>();
        services.AddScoped<IAuthzUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<AuthzUserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<AuthzUserPermissionsProjectionRepository>()
        );
        services.AddScoped<IAuthzRolePermissionsProjectionRepository, AuthzRolePermissionsProjectionRepository>();

        // Fase 5 — el interruptor que consulta NotificationDispatcher antes de cada envío.
        services.AddScoped<IUserNotificationPreferenceRepository, UserNotificationPreferenceRepository>();

        // Reminder Fase 10 — directorio userId → email. El resolver (Application) compone este repo
        // con la recuperación pull contra Auth, que se registra en Program.cs por ser un HttpClient.
        services.AddScoped<IUserEmailDirectoryRepository, UserEmailDirectoryRepository>();
        services.AddScoped<UserEmailResolver>();
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

        // Notification:UsePostmasterDispatch selecciona el gateway de envío:
        // - true (default): publica notifications.email_send_requested.v1 hacia Postmaster; los
        //   callbacks PostmasterEmailDelivery* actualizan el NotificationDispatchAttempt.
        // - false (rollback explícito): gateway in-process, envío via SmtpEmailSender directo —
        //   InProcessEmailDispatchGateway se mantiene como fallback, no se elimina.
        //
        // GetValue<bool> con la clave ausente resuelve a false — por eso el default real está
        // fijado explícitamente en appsettings.json y en el fallback de docker-compose.yml, no
        // solo acá.
        var usePostmasterDispatch = configuration.GetValue<bool>("Notification:UsePostmasterDispatch");
        if (usePostmasterDispatch)
        {
            services.AddScoped<IEmailDispatchGateway, EventBasedEmailDispatchGateway>();
        }
        else
        {
            services.AddScoped<IEmailDispatchGateway, InProcessEmailDispatchGateway>();
        }

        // Mismo flag, segundo punto de invocación: EmailDeliveryService es el transporte real
        // detrás de POST /notifications/email/send y de EmailCampaigns. Se reusa
        // Notification:UsePostmasterDispatch en vez de un flag propio porque ambos interruptores
        // responden la misma pregunta operacional ("¿Postmaster ya es el único transporte de
        // salida de Notification?") — tenerlos separados solo crearía combinaciones a medio
        // migrar sin ningún beneficio real.
        //
        // - true (default): PostmasterEmailDeliveryService — publica
        //   notifications.email_send_requested.v1; los callbacks los resuelve
        //   PostmasterOutboundEmailCallbackConsumers (resuelve contra OutboundEmailMessage, no
        //   contra NotificationLog como el gateway de arriba).
        // - false (rollback explícito): EmailDeliveryService — resuelve EmailProviderConfiguration
        //   propia y envía via ISmtpSendClient/SystemNetSmtpSendClient.
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

        // Módulo de configuración SMTP/API (proveedores de envío). No se retira aunque el default
        // ya sea Postmaster: mientras el flag siga existiendo como rollback,
        // EmailProviderConfigurationRepository/EmailConfigurationResolver/SystemNetSmtpSendClient
        // tienen que seguir registrados y funcionales. También los sigue usando
        // TestEmailConfiguration (POST /notifications/email/configurations/{id}/test), que no pasa
        // por EmailDeliveryService ni por el flag. Retiro completo condicionado a una fase futura,
        // cuando haya confianza operacional real para eliminar InProcessEmailDispatchGateway/
        // EmailDeliveryService y el flag mismo.
        // SmtpEmailSender (IEmailSender, distinto de ISmtpSendClient) no es parte de esta cadena —
        // lo usa InProcessEmailDispatchGateway (el otro path) vía SmtpOptions global, nada que ver
        // con EmailProviderConfiguration por tenant.
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

        AddRateLimitTierQuotas(services, configuration);

        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (mantiene la proyección al día incluso con el flag apagado) y los lectores concretos. El
    // mapeo a ITenantPlanCodeReader/IPlanRateLimitReader (los que RateLimitQuotaResolver
    // realmente consume) es condicional al flag RateLimit:EnforceTierQuotas — decidido en
    // Program.cs, ANTES de AddTieredRateLimiting(). El forwarding de
    // BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer ya existe en Program.cs (no se
    // duplica acá).
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<CachedTenantPlanCodeReader>(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services.AddOptions<SubscriptionClientOptions>().Bind(config.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                var baseUrl = opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/";
                http.BaseAddress = new Uri(baseUrl);
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }
}
