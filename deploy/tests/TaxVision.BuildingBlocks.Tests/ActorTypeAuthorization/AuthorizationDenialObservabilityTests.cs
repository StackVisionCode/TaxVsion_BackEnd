using System.Diagnostics.Metrics;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// A7 — un 403 en producción solo dejaba la línea de ASP.NET ("Authorization failed") y el status:
/// para explicar uno había que reconstruirlo a mano contra el código y la base. Ahora cada denegación
/// deja su razón en el log y en una métrica. El usuario y el tenant van al log —sirven para
/// investigar— pero nunca a la métrica, donde serían cardinalidad y dato sensible.
/// </summary>
[Collection(AuthorizationMetricsCollection.Name)]
public sealed class AuthorizationDenialObservabilityTests : IDisposable
{
    private readonly List<string?> _reasons = [];
    private readonly MeterListener _listener;

    public AuthorizationDenialObservabilityTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AuthorizationMetrics.MeterName && instrument.Name == "authz.denial")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, state) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "reason")
                        _reasons.Add(tag.Value?.ToString());
                }
            }
        );
        _listener.Start();
    }

    private sealed record Entry(LogLevel Level, string Message);

    private sealed class CapturingLogger(List<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => entries.Add(new Entry(logLevel, formatter(state, exception)));
    }

    private sealed class CapturingProvider(List<Entry> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose() { }
    }

    private static (HttpContext Context, List<Entry> Entries) Wire()
    {
        var entries = new List<Entry>();
        var services = new ServiceCollection();
        services.AddSingleton<AuthorizationMetrics>();
        services.AddLogging(builder => builder.AddProvider(new CapturingProvider(entries)));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = "POST";
        context.Request.Path = "/customers/42/addresses";
        return (context, entries);
    }

    [Fact]
    public void A_denial_records_its_reason_as_a_metric()
    {
        var (context, _) = Wire();

        AuthorizationDenial.PermissionDenied.ToProblemDetails(context);
        _listener.RecordObservableInstruments();

        Assert.Contains(AuthorizationDenialReasons.Permission, _reasons);
    }

    [Fact]
    public void A_denial_says_in_the_log_why_and_on_what()
    {
        var (context, entries) = Wire();

        AuthorizationDenial.ForModule("comms").ToProblemDetails(context);

        var entry = Assert.Single(entries);
        Assert.Contains(AuthorizationDenialReasons.Module, entry.Message);
        Assert.Contains("POST", entry.Message);
        Assert.Contains("/customers/42/addresses", entry.Message);
        Assert.Contains("comms", entry.Message);
    }

    [Fact]
    public void A_normal_denial_is_not_an_incident()
    {
        var (context, entries) = Wire();

        AuthorizationDenial.PermissionDenied.ToProblemDetails(context);

        Assert.Equal(LogLevel.Information, Assert.Single(entries).Level);
    }

    [Fact]
    public void An_endpoint_that_declares_no_actor_type_is_a_backend_bug_and_warns()
    {
        var (context, entries) = Wire();

        AuthorizationDenial.ActorTypeNotDeclared.ToProblemDetails(context);

        Assert.Equal(LogLevel.Warning, Assert.Single(entries).Level);
    }

    [Fact]
    public void Without_services_it_still_answers_the_caller()
    {
        // Los filtros la serializan en contextos donde no siempre hay contenedor: observar no puede
        // costar la respuesta.
        var problem = AuthorizationDenial.PermissionDenied.ToProblemDetails(new DefaultHttpContext());

        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    public void Dispose() => _listener.Dispose();
}
