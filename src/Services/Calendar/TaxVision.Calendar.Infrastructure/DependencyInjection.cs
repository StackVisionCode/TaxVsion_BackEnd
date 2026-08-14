using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Application.Permissions.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Application.RateLimiting.Abstractions;
using TaxVision.Calendar.Infrastructure.Customers;
using TaxVision.Calendar.Infrastructure.Jobs;
using TaxVision.Calendar.Infrastructure.Permissions;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Infrastructure.RateLimiting;

namespace TaxVision.Calendar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CalendarDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        // La misma instancia sirve las dos interfaces: el repositorio para los consumers y el lector
        // para ProjectionPermissionsSource, que es quien autoriza sin llamar a Auth.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(p =>
            p.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        AddCustomerDirectory(services, configuration);
        AddRateLimitTierQuotas(services, configuration);
        AddPermissionsPullRecovery(services);

        return services;
    }

    /// <summary>
    /// Escalado de cuotas por tier. Lo de aca esta siempre activo: el consumer de Subscription
    /// mantiene la proyeccion al dia aunque el flag este apagado. El mapeo a los puertos que consume
    /// el resolver vive en Program.cs, condicionado a RateLimit:EnforceTierQuotas.
    /// </summary>
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<ITenantPlanCodeCacheInvalidator, TenantPlanCodeCacheInvalidator>();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
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

    /// <summary>
    /// Recuperacion pull cuando la fuente de permisos encuentra un miss local. Reusa el acquirer ya
    /// registrado: un HttpClient tipado mas, no un segundo acquirer. Sin esto el comportamiento es
    /// fail-closed puro.
    /// </summary>
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

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";

    private static void AddCustomerDirectory(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerDirectoryRepository, CustomerDirectoryRepository>();
        services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();
        services.AddScoped<ITenantCustomerBackfillService, TenantCustomerBackfillService>();

        services.AddOptions<CustomerClientOptions>().Bind(configuration.GetSection(CustomerClientOptions.SectionName));
        services.AddHttpClient<ICalendarCustomerClient, CalendarCustomerClient>(
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

        // Uno rellena nombres faltantes de filas que ya existen; el otro inserta las filas que nunca
        // llegaron. Son huecos distintos y ninguno cubre al otro.
        services.AddHostedService<CustomerDirectoryReconciliationJob>();
        services.AddHostedService<TenantCustomerFullReconciliationJob>();
    }
}
