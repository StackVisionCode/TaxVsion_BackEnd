using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Projections.Abstractions;
using TaxVision.FinancialInsights.Infrastructure.Customers;
using TaxVision.FinancialInsights.Infrastructure.Permissions;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using TaxVision.FinancialInsights.Infrastructure.Persistence.Repositories;
using TaxVision.FinancialInsights.Infrastructure.Processing;
using TaxVision.FinancialInsights.Infrastructure.Upstream;

namespace TaxVision.FinancialInsights.Infrastructure;

/// <summary>
/// DbContext, repositorios, proyecciones (permisos, plan, oficina, clientes y asignaciones) con su reparación,
/// clientes HTTP a CloudStorage/Subscription/Customer y el worker de OCR.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFinancialInsightsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Default is required.");

        services.AddHttpContextAccessor();
        services.AddDbContext<FinancialInsightsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FinancialInsightsDbContext>());

        // Lotes y lecturas del dominio.
        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddScoped<IFinancialMovementRepository, FinancialMovementRepository>();
        services.AddScoped<IAiReservationRepository, AiReservationRepository>();
        services.AddScoped<IFinancialAccessReader, FinancialAccessReader>();
        services.AddScoped<IFinancialProjectionRepository, FinancialProjectionRepository>();
        services.AddScoped<TenantEntitlementsResolver>();
        services
            .AddOptions<FinancialCustomerVisibilityOptions>()
            .Bind(configuration.GetSection(FinancialCustomerVisibilityOptions.SectionName));

        AddPermissionsAndPlan(services, configuration);
        AddUpstreamClients(services, configuration);
        AddCustomerDirectory(services, configuration);
        AddProcessing(services, configuration);
        return services;
    }

    // RBAC por proyección con recuperación pull desde Auth; plan del tenant para el gate y el rate limit.
    private static void AddPermissionsAndPlan(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FinancialUserPermissionsReader>();
        services.AddScoped<IUserPermissionsProjectionReader>(sp => sp.GetRequiredService<FinancialUserPermissionsReader>());
        services.AddScoped<IUserPermissionsProjectionWriter>(sp => sp.GetRequiredService<FinancialUserPermissionsReader>());
        services.AddHttpClient<IPermissionsSnapshotClient, FinancialPermissionsSnapshotClient>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "Services:Auth:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        services.AddHttpClient<IServiceTokenAcquirer, FinancialServiceTokenAcquirer>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "Services:Auth:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);

        services.AddCachedTenantEntitlementModulesReader<FinancialTenantModulesReader>();
        services.AddScoped<FinancialTenantPlanCodeReader>();
        services.AddScoped(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<FinancialTenantPlanCodeReader>()
        ));
        services.AddScoped<ITenantPlanCodeCacheInvalidator, TaxVision.FinancialInsights.Infrastructure.RateLimiting.TenantPlanCodeCacheInvalidator>();
        services.AddHttpClient<HttpPlanRateLimitReader>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "SubscriptionClient:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
    }

    private static void AddUpstreamClients(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IImportFileGateway, CloudStorageImportFileGateway>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "Services:CloudStorage:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        // Plan del tenant por M2M (internal/tenants/{id}/entitlements); el mismo BaseUrl que el catálogo de cuotas.
        services.AddHttpClient<ITenantEntitlementsSnapshotClient, SubscriptionEntitlementsSnapshotClient>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "SubscriptionClient:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        services
            .AddOptions<TenantEntitlementsReconciliationOptions>()
            .Bind(configuration.GetSection(TenantEntitlementsReconciliationOptions.SectionName));
        services.AddHostedService<TenantEntitlementsReconciliationJob>();
    }

    // Directorio de clientes y asignaciones: consumers por eventos + reconciliaciones periódicas desde Customer.
    private static void AddCustomerDirectory(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<CustomerDirectoryReconciliationOptions>()
            .Bind(configuration.GetSection(CustomerDirectoryReconciliationOptions.SectionName));
        services.AddHttpClient<IFinancialCustomerDirectoryClient, FinancialCustomerDirectoryClient>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "Services:Customer:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        services.AddHostedService<TenantCustomerFullReconciliationJob>();
        services.AddHostedService<CustomerDirectoryReconciliationJob>();

        services.AddCustomerVisibilityProjection<FinancialInsightsDbContext>();
        services.AddCustomerVisibilityReconciliation<FinancialPlatformTokenProvider>(configuration);
        // El compose ya define Services:Customer:BaseUrl; se reusa si la sección del kit no trae la suya.
        var customerUrl = configuration["Services:Customer:BaseUrl"];
        if (
            string.IsNullOrWhiteSpace(configuration[$"{CustomerVisibilityReconciliationOptions.SectionName}:CustomerBaseUrl"])
            && !string.IsNullOrWhiteSpace(customerUrl)
        )
            services.PostConfigure<CustomerVisibilityReconciliationOptions>(o => o.CustomerBaseUrl = customerUrl);
    }

    private static void AddProcessing(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FinancialProcessingOptions>(configuration.GetSection(FinancialProcessingOptions.Section));
        services.Configure<OpenAiFinancialOptions>(configuration.GetSection(OpenAiFinancialOptions.Section));
        services.AddScoped<ImportProcessingQueue>();
        services.AddScoped<LocalStatementExtractor>();
        services.AddHttpClient<CloudStorageStatementSource>(http =>
            {
                http.BaseAddress = BaseUri(configuration, "Services:CloudStorage:BaseUrl");
                http.Timeout = TimeSpan.FromSeconds(60);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        services.AddHttpClient<OpenAiStatementClient>(http =>
            {
                http.BaseAddress = new Uri("https://api.openai.com/v1/");
                http.Timeout = TimeSpan.FromSeconds(120);
            })
            .ConfigurePrimaryHttpMessageHandler(NoRedirects);
        services.AddHostedService<FinancialOcrWorker>();
    }

    private static HttpMessageHandler NoRedirects() => new HttpClientHandler { AllowAutoRedirect = false };

    private static Uri? BaseUri(IConfiguration configuration, string key)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Uri.TryCreate(raw.EndsWith('/') ? raw : raw + "/", UriKind.Absolute, out var uri) ? uri : null;
    }
}
