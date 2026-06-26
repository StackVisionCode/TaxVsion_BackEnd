using Microsoft.Extensions.Hosting;
using Serilog;

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
        return host.UseSerilog((context, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate: Template)
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "Logs", $"{serviceName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: Template));
    }
}