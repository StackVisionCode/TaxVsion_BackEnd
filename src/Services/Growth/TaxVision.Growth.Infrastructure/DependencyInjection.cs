using System.Text;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Growth.Infrastructure.Idempotency;
using TaxVision.Growth.Infrastructure.Observability;
using TaxVision.Growth.Infrastructure.Payments;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Permissions.Abstractions;
using TaxVision.Growth.Infrastructure.Persistence.Permissions.Repositories;
using TaxVision.Growth.Infrastructure.Persistence.RateLimiting.Abstractions;
using TaxVision.Growth.Infrastructure.Persistence.RateLimiting.Repositories;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;
using TaxVision.Growth.Infrastructure.RateLimiting;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGrowthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<GrowthDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<GrowthDbContext>());
        services.AddScoped<ICodeDefinitionRepository, CodeDefinitionRepository>();
        services.AddScoped<ICodeQuoteRepository, CodeQuoteRepository>();
        services.AddScoped<ICodeReservationRepository, CodeReservationRepository>();
        services.AddScoped<ICodeRedemptionRepository, CodeRedemptionRepository>();
        services.AddScoped<ICodeCompensationRepository, CodeCompensationRepository>();
        services.AddScoped<ICodeUsageCounterRepository, CodeUsageCounterRepository>();
        services.AddScoped<SqlBusinessIdempotencyExecutor>();
        services.AddScoped<IBusinessIdempotencyExecutor>(provider =>
            provider.GetRequiredService<SqlBusinessIdempotencyExecutor>()
        );
        services.AddScoped<IReferralIdempotencyExecutor, SqlReferralIdempotencyExecutor>();
        services.AddSingleton<ICodeTokenHasher, HmacSha256CodeTokenHasher>();
        services.AddSingleton<IReferralCodeTokenHasher, HmacSha256ReferralCodeTokenHasher>();
        services.AddSingleton<IReferralCodeTokenGenerator, HmacSha256ReferralCodeTokenGenerator>();
        services.AddSingleton<IPaymentOutcomeVerifier, FailClosedPaymentOutcomeVerifier>();
        services.AddScoped<IReferralProgramRepository, ReferralProgramRepository>();
        services.AddScoped<IReferralCodeRepository, ReferralCodeRepository>();
        services.AddScoped<IReferralAttributionRepository, ReferralAttributionRepository>();
        services.AddScoped<IReferralQualificationRepository, ReferralQualificationRepository>();
        services.AddScoped<IReferralRewardCaseRepository, ReferralRewardCaseRepository>();
        services.AddScoped<IReferralRewardAttemptRepository, ReferralRewardAttemptRepository>();
        services.AddScoped<IReferralRewardQuota, SqlReferralRewardQuota>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<GrowthMetrics>();

        // RBAC Fase 7/8 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos consultada por
        // ProjectionPermissionsSource cuando Authorization:PermissionsSource="Projection". La misma
        // instancia scoped satisface el puerto local rico (para los consumers) y el puerto
        // compartido y angosto de BuildingBlocks (para la autorizacion), evitando dos lecturas
        // separadas del mismo dato. Mismo patron que CloudStorage.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        AddRateLimitTierQuotas(services, configuration);

        services
            .AddOptions<CodeTokenHashingOptions>()
            .Bind(configuration.GetSection(CodeTokenHashingOptions.SectionName))
            .Validate(
                value => !string.IsNullOrWhiteSpace(value.Pepper) && Encoding.UTF8.GetByteCount(value.Pepper) >= 32,
                "Growth:Codes:TokenHashing:Pepper must contain at least 32 UTF-8 bytes."
            )
            .ValidateOnStart();
        services
            .AddOptions<ReferralCodeTokenHashingOptions>()
            .Bind(configuration.GetSection(ReferralCodeTokenHashingOptions.SectionName))
            .Validate(
                value => !string.IsNullOrWhiteSpace(value.Pepper) && Encoding.UTF8.GetByteCount(value.Pepper) >= 32,
                "Growth:Referrals:TokenHashing:Pepper must contain at least 32 UTF-8 bytes."
            )
            .ValidateOnStart();
        services
            .AddOptions<BusinessIdempotencyOptions>()
            .Bind(configuration.GetSection(BusinessIdempotencyOptions.SectionName))
            .Validate(
                value => value.RetentionDays is >= 1 and <= 36_500,
                "Growth:BusinessIdempotency:RetentionDays must be between 1 and 36500."
            )
            .ValidateOnStart();
        services
            .AddOptions<PaymentOutcomeVerifierOptions>()
            .Bind(configuration.GetSection(PaymentOutcomeVerifierOptions.SectionName))
            .ValidateOnStart();

        return services;
    }

    // RateLimit Fase 2 — mismo patrón que Connectors: el consumer del evento de Subscription
    // (TenantPlanCodeProjectionConsumer, en Persistence/RateLimiting/Consumers) mantiene la
    // proyección local al día incluso con el flag apagado.
    //
    // Auditoria RateLimit hallazgo #2 — Growth ganó su primera infraestructura de token M2M
    // saliente (ver RateLimiting/ServiceTokenAcquirer.cs); antes solo RECIBIA llamadas M2M
    // (endpoints /internal/*), nunca las hacía. HttpPlanRateLimitReader ahora puede leer el
    // catálogo de Subscription, cerrando el gap documentado en Fase 2.
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader>(
            sp => new BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader(
                sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
                sp.GetRequiredService<EfTenantPlanCodeReader>()
            )
        );
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
