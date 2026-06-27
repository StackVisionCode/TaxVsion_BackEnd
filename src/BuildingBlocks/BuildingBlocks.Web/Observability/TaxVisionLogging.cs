using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace BuildingBlocks.Observability;

public static class TaxVisionLogging
{
    private const string Template =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
        "[{Service}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";

    public static IHostBuilder UseTaxVisionSerilog(
        this IHostBuilder host,
        string serviceName)
    {
        return host.UseSerilog((context, logger) =>
        {
            logger
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .WriteTo.Console(outputTemplate: Template)
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.Combine(AppContext.BaseDirectory, "Logs", $"{serviceName}-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    shared: true);

            var otlpEndpoint = context.Configuration["OpenTelemetry:OtlpEndpoint"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                        ["service.namespace"] = "taxvision",
                        ["deployment.environment"] =
                            context.HostingEnvironment.EnvironmentName
                    };
                });
            }
        });
    }
}
