using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Customers.Abstractions;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Permissions.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Application.RateLimiting.Abstractions;
using TaxVision.Notes.Infrastructure.Customers;
using TaxVision.Notes.Infrastructure.Jobs;
using TaxVision.Notes.Infrastructure.Notes;
using TaxVision.Notes.Infrastructure.Permissions;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Infrastructure.Persistence.Repositories;
using TaxVision.Notes.Infrastructure.RateLimiting;

namespace TaxVision.Notes.Infrastructure;

/// <summary>
/// Fase 2 agregó el DbContext real + INoteRepository. Fase 3 agregó las proyecciones RBAC
/// (User/RolePermissionsProjection). Fase 4 agregó la proyección RateLimit (TenantPlanCodeProjection
/// + acquirer M2M dedicado hacia Subscription); Fase 4B agrega la proyección de Customer + el
/// cliente M2M read-only hacia Customer. Mismo patrón incremental que Correspondence/CloudStorage.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNotesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<NotesDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NotesDbContext>());

        services.AddScoped<INoteRepository, NoteRepository>();
        // Fase 5 — un solo HtmlSanitizer reusado (thread-safe, caro de reconstruir), ver comentario de la clase.
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();

        // RBAC Fase 7 — una sola instancia scoped resuelve el puerto local rico (consumers) y el
        // puerto angosto de BuildingBlocks (ProjectionPermissionsSource), ver comentario de la clase.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        AddRateLimitTierQuotas(services, configuration);
        AddCustomerDirectory(services, configuration);
        AddPermissionsPullRecovery(services);
        return services;
    }

    // Opción B (de este plan) — recuperación pull bajo demanda cuando ProjectionPermissionsSource
    // (BuildingBlocks.Web) encuentra un miss local: un tercer HttpClient tipado hacia Auth (mismo
    // ServiceAuthClientOptions ya bound por AddRateLimitTierQuotas, mismo IServiceTokenAcquirer —
    // no hace falta un cuarto acquirer, ya apunta a Auth). Registrado SOLO en Notes: los otros 9
    // servicios en modo Projection no tocan este DI, así que ProjectionPermissionsSource sigue
    // fail-closed puro para ellos (los dos parámetros nuevos del constructor quedan null).
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

    // Fase 4B (de este plan) — proyección CustomerDirectoryEntry + backfill reactivo (mismo patrón
    // que Correspondence Fase 2) + job de reconciliación periódico. Reutiliza el
    // IServiceTokenAcquirer ya registrado por AddRateLimitTierQuotas (apunta a Auth) — un tercer
    // HttpClient tipado, hacia Customer esta vez, no un segundo acquirer.
    private static void AddCustomerDirectory(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerDirectoryRepository, CustomerDirectoryRepository>();
        services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();
        services.AddScoped<ITenantCustomerBackfillService, TenantCustomerBackfillService>();

        services.AddOptions<CustomerClientOptions>().Bind(configuration.GetSection(CustomerClientOptions.SectionName));
        services.AddHttpClient<INotesCustomerClient, NotesCustomerClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CustomerClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services
            .AddOptions<CustomerDirectoryReconciliationOptions>()
            .Bind(configuration.GetSection(CustomerDirectoryReconciliationOptions.SectionName));
        services.AddHostedService<CustomerDirectoryReconciliationJob>();

        // Backfill de filas faltantes (lo que el job de nombres nunca hizo): barrido completo
        // cross-tenant vía internal/customers/reconciliation. Reusa el HttpClient de NotesCustomerClient
        // y CustomerClientOptions ya bound arriba — solo un hosted service más, sin HttpClient nuevo.
        services.AddHostedService<TenantCustomerFullReconciliationJob>();
    }

    // RateLimit Fase 4 (de este plan) — piezas siempre registradas: el consumer del evento de
    // Subscription (TenantPlanCodeProjectionConsumer, mantiene la proyección al día incluso con el
    // flag apagado) y los lectores concretos de la proyección local. El mapeo a
    // BuildingBlocks.RateLimiting.ITenantPlanCodeReader/IPlanRateLimitReader que
    // RateLimitQuotaResolver realmente consume vive en Program.cs, condicional al flag
    // RateLimit:EnforceTierQuotas.
    //
    // Notes nunca tuvo un IServiceTokenAcquirer M2M propio (Fase 4B, hacia Customer, todavía no
    // existe); se agrega uno dedicado solo para que HttpPlanRateLimitReader pueda leer el catálogo
    // de Subscription — mismo criterio que CloudStorage (auditoría RateLimit hallazgo #2).
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
