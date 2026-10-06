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
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Application.RateLimiting.Abstractions;
using TaxVision.Notification.Infrastructure.Directory;
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

        AddCustomerDirectoryReconciliation(services, configuration);

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
        services.AddScoped<ICustomerEmailDirectoryRepository, CustomerEmailDirectoryRepository>();
        services.AddScoped<UserEmailResolver>();

        // SMS: por defecto se usa el puente real al microservicio Sms (Infobip resuelve proveedor,
        // opt-out, idempotencia). `Notification:UseSmsBridge=false` cae al stub que solo loguea —
        // útil en dev cuando el servicio Sms no está arriba. Default true (a diferencia del push, que
        // arranca en false por requerir credenciales Firebase): el envío por bus es durable, así que
        // aunque Sms esté caído el mensaje espera en la cola sin perderse.
        var useSmsBridge = configuration.GetValue("Notification:UseSmsBridge", true);
        if (useSmsBridge)
        {
            services.AddScoped<ISmsSender, IntegrationEventSmsSender>();
        }
        else
        {
            services.AddScoped<ISmsSender, LoggingSmsSender>();
        }

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

        // Postmaster es el ÚNICO transporte de salida. Hasta 2026-10-02 esto colgaba del flag
        // Notification:UsePostmasterDispatch, cuya rama `false` resolvía contra
        // EmailProviderConfigurations — una tabla vacía en dev y en producción, así que el
        // "rollback" no encendía nada. Un interruptor de emergencia que no funciona es peor que no
        // tenerlo: el día que algo arda, alguien lo gira y pierde una hora entendiendo por qué no
        // pasó nada. Se retiró el flag, sus dos ramas y toda la cadena que las sostenía.
        services.AddScoped<IEmailDispatchGateway, EventBasedEmailDispatchGateway>();
        services.AddScoped<IEmailDeliveryService, PostmasterEmailDeliveryService>();
        services.AddScoped<INotificationLogQueryRepository, NotificationLogQueryRepository>();
        services.AddScoped<IIntegrationEventPublisher, Messaging.WolverineIntegrationEventPublisher>();

        // Cifrado compartido de secretos (Encryption:MasterKey) para configuraciones y tokens.
        services.AddSecretProtection();

        // Módulo de plantillas y layouts (metadata en BD; contenido en CloudStorage). Se conserva
        // por su superficie HTTP de gestión de plantillas (GET/POST /notifications/email/templates),
        // que el frontend consume; ya no lo consume el motor de EmailCampaigns (retirado).
        services.Configure<CloudStorageClientOptions>(configuration.GetSection(CloudStorageClientOptions.SectionName));
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<IEmailLayoutRepository, EmailLayoutRepository>();
        services.AddSingleton<ITemplateRenderer, FluidTemplateRenderer>();
        services.AddScoped<ITemplateStorageService, TemplateStorageService>();
        services.AddScoped<ILayoutStorageService, LayoutStorageService>();

        // Módulo de envío (correos salientes, entrega asíncrona). IEmailDeliveryService se registra
        // más arriba, gateado por Notification:UsePostmasterDispatch (Fase 19) — no acá.
        services.AddScoped<IOutboundEmailRepository, OutboundEmailRepository>();

        // Observabilidad del ciclo de vida de la suscripción (Expiración/Dunning, Fase 6).
        services.AddSingleton<
            TaxVision.Notification.Application.Abstractions.ISubscriptionEmailMetrics,
            Observability.SubscriptionEmailMetrics
        >();

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
    /// <summary>
    /// El directorio de clientes se llenaba sólo por eventos, así que los clientes anteriores al
    /// consumer nunca entraban y un evento perdido dejaba un hueco permanente. Sin nadie que repase
    /// la fuente, el correo al cliente se salta en silencio.
    /// </summary>
    private static void AddCustomerDirectoryReconciliation(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<NotificationCustomerClientOptions>()
            .Bind(configuration.GetSection(NotificationCustomerClientOptions.SectionName));

        services.AddHttpClient<INotificationCustomerClient, NotificationCustomerClient>(
            (sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<NotificationCustomerClientOptions>>().Value;
                var baseUrl = options.BaseUrl.TrimEnd('/');
                http.BaseAddress = new Uri($"{baseUrl}/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services.AddHostedService<CustomerDirectoryReconciliationJob>();
    }

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
