using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BuildingBlocks.Web.Observability;

public static class TaxVisionLogging
{
    private const string Template =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] "
        + "[{Service}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Archivo de logs además de la consola. Por defecto, solo fuera de un contenedor.</summary>
    public const string FileSinkKey = "Observability:Logging:File";

    /// <summary>Logs por OTLP. Apagado: el collector los descarta (nop) hasta que exista un backend.</summary>
    public const string ExportLogsKey = "OpenTelemetry:ExportLogs";

    public static IHostBuilder UseTaxVisionSerilog(this IHostBuilder host, string serviceName)
    {
        host.ConfigureServices(services =>
            services.Configure<RequestLoggingOptions>(options => options.GetLevel = RequestLevel)
        );
        return host.UseSerilog(
            (context, logger) =>
                ConfigureSinks(
                    ConfigureLevels(logger, context.Configuration),
                    context.Configuration,
                    context.HostingEnvironment.EnvironmentName,
                    serviceName
                )
        );
    }

    /// <summary>
    /// Niveles: base en código, después <c>Logging:LogLevel</c> (Serilog no lo lee solo) y al final la sección
    /// <c>Serilog</c>, que gana. Sin esto salía cada SQL de EF y cada paso de ASP.NET a nivel Information.
    /// </summary>
    public static LoggerConfiguration ConfigureLevels(LoggerConfiguration logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);

        foreach (var entry in configuration.GetSection("Logging:LogLevel").GetChildren())
        {
            if (!Enum.TryParse<LogLevel>(entry.Value, ignoreCase: true, out var level))
                continue;
            var mapped = ToSerilog(level);
            if (entry.Key == "Default")
                logger.MinimumLevel.Is(mapped);
            else
                logger.MinimumLevel.Override(entry.Key, mapped);
        }

        return logger.ReadFrom.Configuration(configuration);
    }

    /// <summary>Los chequeos de salud (cada 10 s en compose) no se registran; los errores sí.</summary>
    public static LogEventLevel RequestLevel(HttpContext context, double elapsedMs, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;
        return context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose : LogEventLevel.Information;
    }

    private static LoggerConfiguration ConfigureSinks(
        LoggerConfiguration logger,
        IConfiguration configuration,
        string environmentName,
        string serviceName
    )
    {
        logger
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate: Template);

        // En contenedor Docker ya guarda la consola; el archivo duplicaba todo en un volumen sin lector.
        var inContainer = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
        if (configuration.GetValue(FileSinkKey, !inContainer))
        {
            logger.WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(AppContext.BaseDirectory, "Logs", $"{serviceName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30
            );
        }

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        if (configuration.GetValue(ExportLogsKey, false) && !string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            logger.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName,
                    ["service.namespace"] = "taxvision",
                    ["deployment.environment"] = environmentName,
                };
                // Cola acotada: si el collector cae, no se acumulan 100 000 eventos por servicio.
                options.BatchingOptions.BatchSizeLimit = 500;
                options.BatchingOptions.QueueLimit = 10_000;
                options.BatchingOptions.RetryTimeLimit = TimeSpan.FromMinutes(1);
            });
        }

        return logger;
    }

    private static LogEventLevel ToSerilog(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal,
        };
}
