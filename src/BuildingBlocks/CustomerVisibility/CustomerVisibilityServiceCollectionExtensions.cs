using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.CustomerVisibility;

public static class CustomerVisibilityServiceCollectionExtensions
{
    /// <summary>Registra el store de la proyección compartida sobre el DbContext del servicio (para el consumer
    /// compartido y el filtro de visibilidad). El servicio además debe mapear la entidad
    /// (<c>modelBuilder.ApplyCustomerAssignmentProjection(schema)</c>) + una migración.</summary>
    public static IServiceCollection AddCustomerVisibilityProjection<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext =>
        services.AddScoped<ICustomerAssignmentProjectionStore>(sp => new CustomerAssignmentProjectionStore(
            sp.GetRequiredService<TDbContext>()
        ));

    /// <summary>Registra la reconciliación (pull) que siembra la proyección desde Customer: opciones + el seam
    /// de token del servicio + HttpClient directo a Customer.Api + el hosted job.</summary>
    public static IServiceCollection AddCustomerVisibilityReconciliation<TTokenProvider>(
        this IServiceCollection services,
        IConfiguration configuration
    )
        where TTokenProvider : class, IPlatformServiceTokenProvider
    {
        services
            .AddOptions<CustomerVisibilityReconciliationOptions>()
            .Bind(configuration.GetSection(CustomerVisibilityReconciliationOptions.SectionName));
        services.AddScoped<IPlatformServiceTokenProvider, TTokenProvider>();
        services.AddHttpClient<ICustomerAssignmentsReconciliationClient, CustomerAssignmentsReconciliationClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CustomerVisibilityReconciliationOptions>>().Value;
                var baseUrl = opt.CustomerBaseUrl.EndsWith('/') ? opt.CustomerBaseUrl : opt.CustomerBaseUrl + "/";
                http.BaseAddress = new Uri(baseUrl);
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddHostedService<CustomerAssignmentReconciliationJob>();
        return services;
    }
}
