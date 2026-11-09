using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Infrastructure.Storage;
using TaxVision.Auth.Onboarding.Application;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Sagas.Services;
using TaxVision.Auth.Onboarding.Application.Sessions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Onboarding.Infrastructure.Concurrency;
using TaxVision.Auth.Onboarding.Infrastructure.HttpClients;
using TaxVision.Auth.Onboarding.Infrastructure.Observability;
using TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Onboarding.Infrastructure.Security;
using TaxVision.Auth.Onboarding.Infrastructure.Sessions;

namespace TaxVision.Auth.Onboarding.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra el módulo Onboarding (PayFlow) sobre la infraestructura de Auth, en el mismo host.</summary>
    public static IServiceCollection AddAuthOnboardingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<OnboardingOptions>(configuration.GetSection(OnboardingOptions.SectionName));
        services
            .AddOptions<PaymentAppClientOptions>()
            .Bind(configuration.GetSection(PaymentAppClientOptions.SectionName));
        services
            .AddOptions<DocumentsClientOptions>()
            .Bind(configuration.GetSection(DocumentsClientOptions.SectionName));
        services.AddOptions<TenantClientOptions>().Bind(configuration.GetSection(TenantClientOptions.SectionName));
        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));
        services.AddOptions<GrowthClientOptions>().Bind(configuration.GetSection(GrowthClientOptions.SectionName));

        // Persistencia y sesión del comprador
        services.AddScoped<IEmailVerificationChallengeRepository, EmailVerificationChallengeRepository>();
        services.AddScoped<ITenantOnboardingRepository, TenantOnboardingRepository>();
        services.AddScoped<IOnboardingSubdomainReservationRepository, OnboardingSubdomainReservationRepository>();
        services.AddScoped<ISubdomainHoldReader, OnboardingSubdomainHoldReader>();
        services.AddScoped<IOnboardingSessionStore, RedisOnboardingSessionStore>();
        services.AddScoped<OnboardingSessionService>();
        services.AddScoped<IOnboardingReturnReferenceStore, RedisOnboardingReturnReferenceStore>();

        services.AddSingleton<IOnboardingMetrics, OnboardingMetrics>();
        // Scoped: cada request toma la conexión del DbContext scoped de Auth. El lock es session-scoped
        // en SQL Server, se libera al Dispose. Fallback InMemory queda en tests via WAF override.
        services.AddScoped<IOnboardingReconcileSerializer, SqlOnboardingReconcileSerializer>();
        services.AddSingleton<IOtpCodeGenerator, NumericOtpCodeGenerator>();

        // Un breaker por cliente M2M, ver HttpResiliencePipeline (BuildingBlocks.Infrastructure).
        services.AddSingleton(sp =>
        {
            var metrics = sp.GetRequiredService<IOnboardingMetrics>();
            return new HttpResiliencePipelineRegistry(
                onRetry: metrics.RecordHttpClientRetry,
                onOpened: metrics.RecordHttpClientCircuitOpened
            );
        });

        // Orquestación del checkout y jobs: reserva de códigos, FINALIZE y éxito compartido.
        services.AddScoped<OnboardingCodeReserver>();
        services.AddScoped<OnboardingFinalizer>();
        services.AddScoped<OnboardingSuccessCompleter>();
        services.AddScoped<OnboardingReservationCanceller>();
        services.AddScoped<OnboardingRegistrationReminderProcessor>();
        services.AddScoped<OnboardingExpirationProcessor>();
        services.AddScoped<OnboardingOrphanDetectorProcessor>();
        services.AddScoped<OnboardingRetryProcessor>();

        AddHttpClients(services);
        return services;
    }

    private static void AddHttpClients(IServiceCollection services)
    {
        services.AddHttpClient<IPaymentAppOnboardingClient, PaymentAppOnboardingClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<PaymentAppClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<IGrowthOnboardingClient, GrowthOnboardingClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<GrowthClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<IReceiptDocumentClient, ReceiptDocumentClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<DocumentsClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<ICloudStorageDownloadUrlClient, CloudStorageDownloadUrlClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<ITenantSubdomainAvailabilityClient, TenantSubdomainAvailabilityClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<TenantClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<ITenantProvisioningClient, TenantProvisioningClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<TenantClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<ISubscriptionActivationClient, SubscriptionActivationClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value.BaseUrl, 30)
        );
        services.AddHttpClient<IPlanCatalogClient, PlanCatalogClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value.BaseUrl, 10)
        );
        // Bruto del plan, para cotizar los códigos antes del checkout.
        services.AddHttpClient<IOnboardingPlanPricingClient, OnboardingPlanPricingClient>(
            (sp, http) => Target(http, sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value.BaseUrl, 10)
        );
    }

    private static void Target(HttpClient http, string baseUrl, int timeoutSeconds)
    {
        http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }
}
