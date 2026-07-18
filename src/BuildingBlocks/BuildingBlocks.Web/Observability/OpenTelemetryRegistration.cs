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
        string serviceName
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
                    .AddHttpClientInstrumentation()
                    // ActivitySource propio del servicio (ej. spans manuales de negocio) — mismo
                    // criterio de nombre que el Meter de abajo: serviceName por convención.
                    .AddSource(serviceName);

                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    tracing.AddOtlpExporter(options => options.Endpoint = uri);
            })
            .WithMetrics(metrics =>
            {
                // Meter propio del servicio (ej. PostmasterMetrics) — nombre = serviceName por convención,
                // así cada servicio solo necesita registrar sus Counters/Gauges, sin tocar este builder compartido.
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(serviceName);

                if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    metrics.AddOtlpExporter(options => options.Endpoint = uri);
            });

        return services;
    }
}
