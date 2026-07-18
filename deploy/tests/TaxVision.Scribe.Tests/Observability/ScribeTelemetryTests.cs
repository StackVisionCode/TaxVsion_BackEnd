using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Observability;
using TaxVision.Scribe.Infrastructure.Rendering;
using TaxVision.Scribe.Tests.Rendering;

namespace TaxVision.Scribe.Tests.Observability;

/// <summary>
/// Fase 6, plan §36 ítems 2-3 — scribe_render_requests_total/scribe_render_duration_seconds y un span
/// por render. ScribeTelemetry usa un Meter/ActivitySource estáticos de proceso (mismo criterio que
/// PostmasterMetrics) — como xUnit corre clases de test en paralelo, cada test acá usa un TemplateKey
/// único (GUID) y filtra las mediciones por ese tag para no capturar renders de otros tests/clases
/// corriendo al mismo tiempo, y colecciones thread-safe porque los callbacks del listener corren en
/// el hilo que dispara la métrica, no en el hilo del test.
/// </summary>
public sealed class ScribeTelemetryTests
{
    private static readonly EventKey TestEventKey = EventKey.Create("auth.password_reset_requested.v1").Value;
    private static readonly LayoutKey TestLayoutKey = LayoutKey.Create("system-base").Value;

    private sealed class FakeEventTemplateMappingRepository(TemplateKey templateKey) : IEventTemplateMappingRepository
    {
        public Task AddAsync(EventTemplateMapping mapping, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<BuildingBlocks.Results.Result<EventTemplateMapping>> GetByIdAsync(
            Guid id,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<EventTemplateMapping>> ListAsync(Guid? tenantId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<EventTemplateMapping>> GetEnabledForEventAsync(
            EventKey eventKey,
            Guid? tenantId,
            CancellationToken ct = default
        )
        {
            var mapping = EventTemplateMapping
                .CreateNew(TemplateScope.System, null, TestEventKey, templateKey, null, priority: 0, DateTime.UtcNow)
                .Value;
            return Task.FromResult<IReadOnlyList<EventTemplateMapping>>([mapping]);
        }

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static (FluidTemplateRenderer Renderer, TemplateKey TemplateKey) BuildRenderer()
    {
        var uniqueTemplateKey = TemplateKey.Create($"auth.password_reset.{Guid.NewGuid():N}").Value;

        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                uniqueTemplateKey,
                "Password reset",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var layout = EmailLayout
            .CreateNew(TemplateScope.System, null, TestLayoutKey, "System base", null, Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        var layoutHtmlFileId = Guid.NewGuid();
        var layoutVersion = layout
            .AddDraftVersion("layout.html", layoutHtmlFileId, null, null, null, null, DateTime.UtcNow)
            .Value;
        layout.PublishVersion(layoutVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        var htmlFileId = Guid.NewGuid();
        var templateVersion = template
            .AddDraftVersion(
                "Reset your password, {{ name }}",
                "template.html",
                htmlFileId,
                null,
                null,
                null,
                null,
                null,
                null,
                layout.Id,
                layoutVersion.VersionNumber,
                [],
                DateTime.UtcNow
            )
            .Value;
        template.PublishVersion(templateVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        var cloudStorage = new FakeCloudStorageClient();
        cloudStorage.Seed(htmlFileId, "<p>Hi {{ name }}</p>");
        cloudStorage.Seed(layoutHtmlFileId, "<html><body>{{ body | raw }}</body></html>");

        var logo = new LogoAsset(Guid.NewGuid(), "image/png", 1024, IsFallback: false);
        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new FakeEventTemplateMappingRepository(uniqueTemplateKey)),
            new FakeEmailTemplateRepository(template),
            new FakeEmailLayoutRepository(layout),
            cloudStorage,
            new FakeLogoResolver(logo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeTemplateSourceCache(),
            NullLogger<FluidTemplateRenderer>.Instance
        );
        return (renderer, uniqueTemplateKey);
    }

    private static RenderRequest BuildRequest() =>
        new(
            TestEventKey,
            TenantId: null,
            Locale: null,
            Variables: new Dictionary<string, object?> { ["name"] = "Ana" }
        );

    [Fact]
    public async Task RenderAsync_records_a_render_request_metric_tagged_miss_on_a_cold_cache()
    {
        var (renderer, templateKey) = BuildRenderer();
        var measurements = new ConcurrentBag<(long Value, string CacheLayer, string Scope, string Tenant)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "scribe-service" && instrument.Name == "scribe_render_requests_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, state) =>
            {
                var tagArray = tags.ToArray();
                string GetTag(string key) => tagArray.First(t => t.Key == key).Value?.ToString() ?? "";
                if (GetTag("template_key") == templateKey.Value)
                    measurements.Add((value, GetTag("cache_layer"), GetTag("scope"), GetTag("tenant")));
            }
        );
        listener.Start();

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("miss", measurement.CacheLayer);
        Assert.Equal("System", measurement.Scope);
        Assert.Equal("system", measurement.Tenant);
    }

    [Fact]
    public async Task RenderAsync_records_cache_layer_l1_once_everything_is_warm()
    {
        var (renderer, templateKey) = BuildRenderer();
        await renderer.RenderAsync(BuildRequest());

        var measurements = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "scribe-service" && instrument.Name == "scribe_render_requests_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, state) =>
            {
                var tagArray = tags.ToArray();
                if (tagArray.First(t => t.Key == "template_key").Value?.ToString() == templateKey.Value)
                    measurements.Add(tagArray.First(t => t.Key == "cache_layer").Value?.ToString() ?? "");
            }
        );
        listener.Start();

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(["l1"], measurements);
    }

    [Fact]
    public async Task RenderAsync_records_a_render_duration_measurement()
    {
        var (renderer, templateKey) = BuildRenderer();
        var durations = new ConcurrentBag<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "scribe-service" && instrument.Name == "scribe_render_duration_seconds")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, state) =>
            {
                var tagArray = tags.ToArray();
                if (tagArray.First(t => t.Key == "template_key").Value?.ToString() == templateKey.Value)
                    durations.Add(value);
            }
        );
        listener.Start();

        await renderer.RenderAsync(BuildRequest());

        var duration = Assert.Single(durations);
        Assert.True(duration >= 0);
    }

    [Fact]
    public async Task RenderAsync_starts_a_span_for_the_render()
    {
        var (renderer, templateKey) = BuildRenderer();
        // El tag template_key se setea DESPUÉS de StartActivity (en el caller), así que filtrar
        // en ActivityStarted siempre daría vacío — se recolecta todo y se filtra por tag recién
        // al final, cuando SetTag ya corrió.
        var startedActivities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "scribe-service",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => startedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        await renderer.RenderAsync(BuildRequest());

        var activity = Assert.Single(
            startedActivities,
            a => a.GetTagItem("template_key")?.ToString() == templateKey.Value
        );
        Assert.Equal("scribe.render", activity.OperationName);
    }
}
