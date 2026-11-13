using BuildingBlocks.Web.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Observability;

/// <summary>
/// Con <c>UseSerilog</c>, la sección <c>Logging:LogLevel</c> se ignoraba: producción registraba cada SQL de EF y
/// cada paso de ASP.NET a nivel Information. Ahora la base es Warning para Microsoft/EF, se respeta
/// <c>Logging:LogLevel</c> y la sección <c>Serilog</c> sigue ganando.
/// </summary>
public sealed class TaxVisionLoggingTests
{
    [Fact]
    public void Ef_sql_commands_are_not_logged_at_information()
    {
        using var host = Host(new() { ["Logging:LogLevel:Default"] = "Information" });
        var factory = host.Services.GetRequiredService<ILoggerFactory>();

        Assert.False(
            factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Information)
        );
        Assert.False(factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Information));
        Assert.True(factory.CreateLogger("Microsoft.Hosting.Lifetime").IsEnabled(LogLevel.Information));
        Assert.True(factory.CreateLogger("TaxVision.Tasks.Application").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void Logging_log_level_section_is_honored()
    {
        using var host = Host(new() { ["Logging:LogLevel:TaxVision.Noisy"] = "Error" });
        var factory = host.Services.GetRequiredService<ILoggerFactory>();

        Assert.False(factory.CreateLogger("TaxVision.Noisy.Thing").IsEnabled(LogLevel.Warning));
        Assert.True(factory.CreateLogger("TaxVision.Noisy.Thing").IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void Serilog_section_wins_to_see_sql_temporarily()
    {
        using var host = Host(
            new()
            {
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
            }
        );
        var factory = host.Services.GetRequiredService<ILoggerFactory>();

        Assert.True(
            factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Information)
        );
    }

    [Theory]
    [InlineData("/health/ready", 200, LogEventLevel.Verbose)]
    [InlineData("/health/live", 200, LogEventLevel.Verbose)]
    [InlineData("/tasks/board", 200, LogEventLevel.Information)]
    [InlineData("/tasks/board", 500, LogEventLevel.Error)]
    [InlineData("/health/ready", 503, LogEventLevel.Error)]
    public void Health_checks_stay_out_of_the_request_log(string path, int status, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        Assert.Equal(expected, TaxVisionLogging.RequestLevel(context, 1, null));
    }

    private static IHost Host(Dictionary<string, string?> settings)
    {
        settings[TaxVisionLogging.FileSinkKey] = "false";
        return Microsoft
            .Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .UseTaxVisionSerilog("logging-test")
            .Build();
    }
}
