using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Scribe;

public static class ScribeClientRegistration
{
    public static IServiceCollection AddScribeRenderClient(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<ScribeClientOptions>(configuration.GetSection(ScribeClientOptions.SectionName));
        services.AddHttpClient<IScribeRenderClient, ScribeRenderClient>(
            (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<ScribeClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            }
        );
        return services;
    }
}
