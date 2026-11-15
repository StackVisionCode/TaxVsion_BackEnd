using BuildingBlocks.Web.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Observability;

/// <summary>
/// El collector descarta las trazas (exporter nop): generarlas y mandarlas solo gastaba CPU y red. Se encienden
/// con <c>OpenTelemetry:ExportTraces=true</c> cuando haya backend; las métricas siguen siempre.
/// </summary>
public sealed class OpenTelemetryRegistrationTests
{
    [Fact]
    public void Traces_are_off_unless_they_are_exported()
    {
        using var provider = Build(new() { ["OpenTelemetry:OtlpEndpoint"] = "http://otel-collector:4317" });

        Assert.Null(provider.GetService<TracerProvider>());
        Assert.NotNull(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void Traces_turn_on_with_the_flag()
    {
        using var provider = Build(
            new()
            {
                ["OpenTelemetry:OtlpEndpoint"] = "http://otel-collector:4317",
                [OpenTelemetryRegistration.ExportTracesKey] = "true",
            }
        );

        Assert.NotNull(provider.GetService<TracerProvider>());
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddTaxVisionOpenTelemetry(configuration, "otel-test")
            .BuildServiceProvider();
    }
}
