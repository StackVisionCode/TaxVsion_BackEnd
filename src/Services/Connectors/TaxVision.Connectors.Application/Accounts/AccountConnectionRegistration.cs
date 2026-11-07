using Microsoft.Extensions.DependencyInjection;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Colaboradores de los handlers de conectar cuenta (CON-13).</summary>
public static class AccountConnectionRegistration
{
    public static IServiceCollection AddAccountConnectionFlow(this IServiceCollection services)
    {
        services.AddScoped<AccountProviderSwitcher>();
        services.AddScoped<AccountConnectionFinisher>();
        services.AddScoped<OAuthGrantStore>();
        services.AddScoped<ManualAccountProvisioner>();
        return services;
    }
}
