using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.Security;

public static class ProtectedPayloadRegistration
{
    public static IServiceCollection AddProtectedPayloadProtection(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddRotatingSecretProtection(configuration);
        services.TryAddSingleton<IProtectedPayloadCodec, JsonProtectedPayloadCodec>();
        return services;
    }
}
