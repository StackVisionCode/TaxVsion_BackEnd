using System.Diagnostics.Metrics;
using BuildingBlocks.Web.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using TaxVision.Calendar.Infrastructure.Observability;
using Xunit;

namespace TaxVision.Calendar.Tests.Observability;

/// <summary>
/// El fallo que este test existe para evitar: un Meter con nombre propio que no se registre como
/// <c>additionalMeterNames</c> <b>no exporta nada</b>. Los contadores suben en memoria, el código
/// parece instrumentado y el dashboard queda vacío sin un solo error.
///
/// <para>
/// Se mide con un exporter real en memoria, no leyendo la lista de nombres: lo que importa es que la
/// medición salga por el otro extremo.
/// </para>
/// </summary>
public sealed class MeterIsExportedTests
{
    [Fact]
    public void The_calendar_meter_reaches_the_exporter()
    {
        var exported = Collect(withCalendarMeter: true);

        Assert.Contains(exported, m => m.Name == "appointment.created_total");
    }

    /// <summary>La contraparte: sin el nombre, la misma medición no llega a ningún lado.</summary>
    [Fact]
    public void Without_the_meter_name_nothing_is_exported()
    {
        var exported = Collect(withCalendarMeter: false);

        // Y el exporter no está mudo: si no saliera nada, este test pasaría por la razón equivocada.
        Assert.NotEmpty(exported);
        Assert.DoesNotContain(exported, m => m.Name == "appointment.created_total");
    }

    private static List<Metric> Collect(bool withCalendarMeter)
    {
        var exported = new List<Metric>();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var meterNames = withCalendarMeter ? new[] { CalendarMetrics.MeterName } : [];
        services.AddTaxVisionOpenTelemetry(new ConfigurationBuilder().Build(), "calendar-service-test", meterNames);
        services.ConfigureOpenTelemetryMeterProvider(builder => builder.AddInMemoryExporter(exported));

        using var provider = services.BuildServiceProvider();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        using var metrics = new CalendarMetrics();
        metrics.RecordCreated(isRecurring: false);

        meterProvider.ForceFlush();
        return exported;
    }
}
