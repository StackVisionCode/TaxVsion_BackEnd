using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaxVision.Gateway.LoadShedding;

public static class LoadSheddingRegistration
{
    public static IServiceCollection AddLoadShedding(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LoadShedderOptions>(configuration.GetSection(LoadShedderOptions.SectionName));

        var windowSeconds =
            configuration.GetValue<int?>($"{LoadShedderOptions.SectionName}:WindowSeconds")
            ?? new LoadShedderOptions().WindowSeconds;

        services.AddSingleton(new RequestOutcomeWindow(windowSeconds));
        services.AddSingleton(new TenantConsumptionTracker(windowSeconds));
        services.AddSingleton<ILoadShedder, LoadShedder>();

        return services;
    }
}
