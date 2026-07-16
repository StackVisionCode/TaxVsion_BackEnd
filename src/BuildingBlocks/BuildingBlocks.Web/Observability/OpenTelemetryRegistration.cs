using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Observability;

public static class OpenTelemetryRegistration
{
    public static IServiceCollection AddTaxVisionOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        params string[] additionalMeterNames
    )
    {
        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName: serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    tracing.AddOtlpExporter(options => options.Endpoint = uri);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();

                foreach (var meterName in additionalMeterNames)
                    metrics.AddMeter(meterName);

                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    metrics.AddOtlpExporter(options => options.Endpoint = uri);
            });

        return services;
    }
}
