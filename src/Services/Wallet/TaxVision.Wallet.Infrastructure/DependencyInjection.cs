using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Wallet.Application.Permissions.Abstractions;
using TaxVision.Wallet.Application.RateLimiting.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Infrastructure.Permissions;
using TaxVision.Wallet.Infrastructure.Persistence;
using TaxVision.Wallet.Infrastructure.Persistence.Repositories;
using TaxVision.Wallet.Infrastructure.RateLimiting;

namespace TaxVision.Wallet.Infrastructure;

/// <summary>
/// F1 (00_Plan_And_Architecture.md §9) — esqueleto clonado de Notes. Registra el DbContext +
/// repositorios del monedero, las proyecciones RBAC (User/RolePermissionsProjection) para
/// <c>[HasPermission]</c>, la proyección RateLimit (TenantPlanCodeProjection + acquirer M2M hacia
/// Subscription) y la recuperación pull de permisos. Sin lógica de cobro aún (F2/F3).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWalletInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<WalletDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<WalletDbContext>());

        // F1 — repos de dominio del monedero.
        services.AddScoped<IWalletRepository, WalletRepository>();
        // F3 — catálogo de precios versionado.
        services.AddScoped<TaxVision.Wallet.Application.Pricing.IPriceBookRepository, Persistence.Repositories.PriceBookRepository>();
        // F4 — reservas del PEP (money-OUT): hold por run + liquidación al cierre.
        services.AddScoped<TaxVision.Wallet.Application.Reservations.Abstractions.IReservationRepository, Persistence.Repositories.ReservationRepository>();

        // Recarga (money-IN) por checkout HOSTEADO: cliente M2M hacia PaymentApp (crea la sesión de pago en
        // Stripe/PayPal; nunca se guarda tarjeta). Reusa el IServiceTokenAcquirer de abajo (actor_type=Service).
        services
            .AddOptions<PaymentApp.PaymentAppClientOptions>()
            .Bind(configuration.GetSection(PaymentApp.PaymentAppClientOptions.SectionName));
        services.AddHttpClient<
            TaxVision.Wallet.Application.Wallet.Abstractions.IWalletTopUpCheckoutClient,
            PaymentApp.WalletTopUpCheckoutClient
        >(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<PaymentApp.PaymentAppClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // RBAC — una sola instancia scoped resuelve el puerto local rico (consumers) y el puerto
        // angosto de BuildingBlocks (ProjectionPermissionsSource).
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        AddRateLimitTierQuotas(services, configuration);
        AddPermissionsPullRecovery(services);
        return services;
    }

    // Recuperación pull bajo demanda cuando ProjectionPermissionsSource (BuildingBlocks.Web)
    // encuentra un miss local: un HttpClient tipado hacia Auth (mismo ServiceAuthClientOptions ya
    // bound por AddRateLimitTierQuotas, mismo IServiceTokenAcquirer — ya apunta a Auth).
    private static void AddPermissionsPullRecovery(IServiceCollection services)
    {
        services.AddScoped<IUserPermissionsProjectionWriter, PermissionsProjectionWriter>();
        services.AddHttpClient<IPermissionsSnapshotClient, PermissionsSnapshotClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );
    }

    // RateLimit — proyección local de PlanCode + lectores + consumer del evento de Subscription +
    // acquirer M2M dedicado para que HttpPlanRateLimitReader lea el catálogo de Subscription. El
    // mapeo a BuildingBlocks.RateLimiting.ITenantPlanCodeReader/IPlanRateLimitReader vive en
    // Program.cs, condicional al flag RateLimit:EnforceTierQuotas.
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        // Gate de módulo — lector de módulos habilitados (fuente en Program.cs).
        services.AddCachedTenantEntitlementModulesReader<TaxVision.Wallet.Infrastructure.RateLimiting.EfTenantEntitlementModulesReader>();
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
            .AddOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>()
            .Bind(
                configuration.GetSection(
                    BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions.SectionName
                )
            );
        services.AddHttpClient<BuildingBlocks.Infrastructure.RateLimiting.HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<
                    IOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>
                >().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
