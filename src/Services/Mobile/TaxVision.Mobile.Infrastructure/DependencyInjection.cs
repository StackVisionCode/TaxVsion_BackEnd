using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Mobile.Application.Projections.Abstractions;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Infrastructure.Permissions;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMobileInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connection =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");
        services.AddDbContext<MobileDbContext>(options => options.UseSqlServer(connection));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MobileDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<MobileProjectionRepository>();
        services.AddScoped<IMobileProjectionRepository>(sp => sp.GetRequiredService<MobileProjectionRepository>());
        services.AddScoped<IUserPermissionsProjectionReader>(sp => sp.GetRequiredService<MobileProjectionRepository>());
        services.AddScoped<IUserPermissionsProjectionWriter>(sp => sp.GetRequiredService<MobileProjectionRepository>());
        void ConfigureAuth(HttpClient http)
        {
            http.BaseAddress = new Uri(configuration["ServiceAuthClient:AuthBaseUrl"] ?? "http://localhost:5124/");
            http.Timeout = TimeSpan.FromSeconds(15);
        }
        services
            .AddHttpClient<IServiceTokenAcquirer, MobileServiceTokenAcquirer>(ConfigureAuth)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services
            .AddHttpClient<IPermissionsSnapshotClient, MobilePermissionsSnapshotClient>(ConfigureAuth)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
