using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.RateLimiting.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Infrastructure.Growth;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.RateLimiting;
using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSubscriptionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<SubscriptionDbContext>(options => options.UseSqlServer(connectionString));
        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.SectionName));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SubscriptionDbContext>());
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<ISubscriptionSeatRepository, SubscriptionSeatRepository>();
        services.AddScoped<ISubscriptionTenantSettingsRepository, SubscriptionTenantSettingsRepository>();
        services.AddScoped<IAddOnDefinitionRepository, AddOnDefinitionRepository>();
        services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
        services.AddScoped<ITenantEntitlementSnapshotRepository, TenantEntitlementSnapshotRepository>();
        services.AddScoped<ISubscriptionAuditLogWriter, SubscriptionAuditLogWriter>();
        services.AddScoped<IPlanRateLimitRepository, PlanRateLimitRepository>();
        services.AddScoped<ISubscriptionAuditLogRepository, SubscriptionAuditLogRepository>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379")
        );
        services.AddSingleton<IDistributedLockFactory, RedisDistributedLockFactory>();

        // RBAC Fase 7 (RBAC_Hardening_Plan.md) -- Subscription solo recibe la proyeccion de
        // sincronizacion (sin wiring de enforcement: no usa [HasPermission]/PermissionPolicyProvider
        // todavia, eso es Fase 8). Se construye ahora para que ya este al dia cuando esa fase active
        // el mecanismo.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        // Fase 4 Referidos (2026-07-21) — M2M contra Growth para reservar el descuento de
        // bienvenida del referido antes de la primera activación. Mismo patrón que
        // CorrespondenceServiceTokenAcquirer/CorrespondenceCustomerClient.
        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddOptions<GrowthClientOptions>().Bind(configuration.GetSection(GrowthClientOptions.SectionName));

        services.AddHttpClient<IGrowthServiceTokenAcquirer, GrowthServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddHttpClient<IReferralBenefitReserver, GrowthRefereeBenefitClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<GrowthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        AddRateLimitTierQuotas(services);
        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento
    // TenantEntitlementsChangedIntegrationEvent (que este mismo servicio publica; ver remarks en
    // TenantPlanCodeProjection) mantiene la proyección local al día incluso con el flag apagado,
    // y los lectores concretos de esa proyección. NO se registra acá el mapeo a
    // BuildingBlocks.RateLimiting.ITenantPlanCodeReader (eso vive en Program.cs, condicional al
    // flag RateLimit:EnforceTierQuotas, como en Customer/Tenant/Connectors).
    //
    // Caso especial de Subscription para IPlanRateLimitReader/HttpPlanRateLimitReader: este
    // servicio ES el dueño de la tabla PlanRateLimits y expone el propio endpoint M2M
    // (GET subscriptions/internal/plan-rate-limits) que HttpPlanRateLimitReader llama en TODOS
    // los demás servicios. Apuntar HttpPlanRateLimitReader a sí mismo implicaría un round-trip
    // HTTP + adquisición de token M2M (vía IGrowthServiceTokenAcquirer, que sí existe acá pero
    // está atado al BaseUrl de Auth para llamar a Growth, no es un acquirer genérico) para leer un
    // dato que ya está disponible en el mismo proceso vía IPlanRateLimitRepository/
    // GetPlanRateLimitsHandler (subscriptions/internal/plan-rate-limits ya delega en él) —
    // circular y más lento que una lectura directa, sin ningún beneficio de desacople porque no
    // hay otro servicio de por medio. Escribir un adaptador NUEVO tipo
    // "DirectPlanRateLimitReader" sobre IPlanRateLimitRepository (mismo shape que
    // HttpPlanRateLimitReader.FetchCatalogAsync pero sin HTTP) es técnicamente sencillo, pero no
    // existe ningún precedente de un IPlanRateLimitReader en memoria/DB local en el resto de la
    // flota (todos los demás servicios son consumidores HTTP) — sin ese precedente exacto para
    // replicar, inventar esa pieza queda fuera del alcance mecánico de esta sub-fase (ver
    // guardrails del plan RateLimit Fase 2 / feedback_no_speculative_vendor_coupling). Se deja
    // como gap documentado, igual que CloudStorage/Connectors: si RateLimit:EnforceTierQuotas se
    // activa acá sin cerrar este gap, TieredRateLimitingRegistration.AddTieredRateLimiting() cae
    // en NullPlanRateLimitReader vía TryAddSingleton (fail-open a la cuota base sin escalar por
    // plan) — degradado pero seguro, nunca un crash.
    private static void AddRateLimitTierQuotas(IServiceCollection services)
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
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
