using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.RateLimiting.Abstractions;
using TaxVision.Billing.Infrastructure.Documents;
using TaxVision.Billing.Infrastructure.Observability;
using TaxVision.Billing.Infrastructure.Payments;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using TaxVision.Billing.Infrastructure.RateLimiting;
using TaxVision.Billing.Infrastructure.ServiceAuth;

namespace TaxVision.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<BillingDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<BillingDbContext>());
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IIssuerProfileRepository, IssuerProfileRepository>();
        services.AddScoped<IInvoiceNumberSequenceRepository, InvoiceNumberSequenceRepository>();
        services.AddScoped<IPaymentReceiptRepository, PaymentReceiptRepository>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<BillingMetrics>();

        // --- Tokens M2M: un solo proveedor para todos los clientes de servicio (punto 10 del review) ---
        services
            .AddOptions<BillingServiceClientsOptions>()
            .Bind(configuration.GetSection(BillingServiceClientsOptions.SectionName));

        services.AddHttpClient<IServiceTokenProvider, ServiceTokenProvider>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<BillingServiceClientsOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // --- M2M hacia Documents (generación del PDF de factura) ---
        services
            .AddOptions<BillingDocumentsOptions>()
            .Bind(configuration.GetSection(BillingDocumentsOptions.SectionName));
        services.AddHttpClient<IInvoiceDocumentClient, BillingDocumentsClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<BillingDocumentsOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // --- M2M hacia PaymentClient (ensure del ancla de cobro de la factura, Fase 2A) ---
        services
            .AddOptions<BillingPaymentClientOptions>()
            .Bind(configuration.GetSection(BillingPaymentClientOptions.SectionName));
        services.AddHttpClient<IInvoicePaymentLinkClient, BillingPaymentClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<BillingPaymentClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        AddRateLimitTierQuotas(services, configuration);

        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (mantiene la proyección al día incluso con el flag apagado) y los lectores concretos. El
    // mapeo a ITenantPlanCodeReader/IPlanRateLimitReader (los que RateLimitQuotaResolver
    // realmente consume) es condicional al flag RateLimit:EnforceTierQuotas — decidido en
    // Program.cs, ANTES de AddTieredRateLimiting().
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration config)
    {
        // HttpPlanRateLimitReader (BuildingBlocks.Infrastructure.RateLimiting) depende del
        // contrato compartido; a diferencia de Tenant/Customer, Billing no tiene un acquirer de
        // un solo cliente — tiene IServiceTokenProvider (multi-cliente nombrado). El adaptador
        // fija el nombre de cliente "Subscription" (ver SubscriptionServiceTokenAcquirer).
        services.AddTransient<BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer>(
            sp => new SubscriptionServiceTokenAcquirer(sp.GetRequiredService<IServiceTokenProvider>())
        );

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
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
