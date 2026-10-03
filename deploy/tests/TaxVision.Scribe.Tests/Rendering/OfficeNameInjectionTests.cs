using BuildingBlocks.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Rendering;

namespace TaxVision.Scribe.Tests.Rendering;

/// <summary>
/// El nombre de la oficina lo pone el renderer desde la proyección, no el caller. Llegó a producción
/// un "here is your invoice from ." porque Billing lo sacaba de su <c>IssuerProfile</c>, que esa
/// oficina nunca llenó.
///
/// <para>Se comprueba en el asunto, el cuerpo y el layout: los tres lo usan y los tres salen del mismo
/// diccionario. Un test que mirara solo el layout pasaría con el asunto roto.</para>
/// </summary>
public sealed class OfficeNameInjectionTests
{
    private static readonly EventKey EventKeyValue = EventKey.Create("billing.invoice_sent.v1").Value;
    private static readonly TemplateKey TemplateKeyValue = TemplateKey.Create("billing.invoice_sent").Value;
    private static readonly LogoAsset Logo = new(Guid.NewGuid(), "image/png", 512, IsFallback: false);

    [Fact]
    public async Task The_office_name_reaches_the_subject_the_body_and_the_layout()
    {
        var tenantId = Guid.NewGuid();
        var profiles = new FakeTenantProfileRefRepository();
        profiles.Names[tenantId] = "Manfer Tax Office";

        var result = await RenderAsync("tenant-base", tenantId, profiles, callerName: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Invoice from Manfer Tax Office", result.Value.Subject);
        Assert.Contains("body: Manfer Tax Office", result.Value.Html);
        Assert.Contains("footer: Manfer Tax Office", result.Value.Html);
    }

    [Fact]
    public async Task The_projection_wins_over_what_the_caller_sent()
    {
        // El caller no es la fuente de verdad del nombre de la oficina; Tenant lo es.
        var tenantId = Guid.NewGuid();
        var profiles = new FakeTenantProfileRefRepository();
        profiles.Names[tenantId] = "Manfer Tax Office";

        var result = await RenderAsync("tenant-base", tenantId, profiles, callerName: "Lo que mando Billing");

        Assert.True(result.IsSuccess);
        Assert.Contains("body: Manfer Tax Office", result.Value.Html);
    }

    [Fact]
    public async Task Without_a_projected_office_the_callers_value_still_applies()
    {
        // Oficina creada antes del backfill: mejor el dato del caller que un correo sin firma.
        var result = await RenderAsync("tenant-base", Guid.NewGuid(), new(), callerName: "Fallback Co");

        Assert.True(result.IsSuccess);
        Assert.Contains("body: Fallback Co", result.Value.Html);
    }

    [Fact]
    public async Task A_platform_email_does_not_get_an_office_name()
    {
        // system-base es la cáscara de la plataforma: inyectarle el nombre de una oficina la haría
        // firmar como si el correo fuera de ella.
        var tenantId = Guid.NewGuid();
        var profiles = new FakeTenantProfileRefRepository();
        profiles.Names[tenantId] = "Manfer Tax Office";

        var result = await RenderAsync("system-base", tenantId, profiles, callerName: null);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Manfer Tax Office", result.Value.Html);
    }

    private static async Task<Result<RenderedContent>> RenderAsync(
        string layoutKey,
        Guid tenantId,
        FakeTenantProfileRefRepository profiles,
        string? callerName
    )
    {
        var htmlFileId = Guid.NewGuid();
        var layoutHtmlFileId = Guid.NewGuid();

        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKeyValue,
                "Invoice sent",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.System,
                null,
                LayoutKey.Create(layoutKey).Value,
                layoutKey,
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        var layoutVersion = layout
            .AddDraftVersion(
                $"system/layouts/{layoutKey}/v1/layout.html",
                layoutHtmlFileId,
                null,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        layout.PublishVersion(layoutVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        var templateVersion = template
            .AddDraftVersion(
                "Invoice from {{ tenant_name }}",
                "system/templates/billing.invoice_sent/v1/template.html",
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
        cloudStorage.Seed(htmlFileId, "<p>body: {{ tenant_name }}</p>");
        cloudStorage.Seed(
            layoutHtmlFileId,
            "<html><body>{{ body | raw }}<i>footer: {{ tenant_name }}</i></body></html>"
        );

        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new SingleMappingRepository(TemplateKeyValue)),
            new FakeEmailTemplateRepository(template),
            new FakeEmailLayoutRepository(layout),
            cloudStorage,
            new FakeLogoResolver(Logo),
            profiles,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeTemplateSourceCache(),
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var variables = new Dictionary<string, object?>();
        if (callerName is not null)
            variables["tenant_name"] = callerName;

        return await renderer.RenderAsync(new RenderRequest(EventKeyValue, tenantId, Locale: null, variables));
    }

    private sealed class SingleMappingRepository(TemplateKey templateKey) : IEventTemplateMappingRepository
    {
        public Task AddAsync(EventTemplateMapping mapping, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<EventTemplateMapping>> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<EventTemplateMapping>> ListAsync(Guid? tenantId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<EventTemplateMapping>> GetEnabledForEventAsync(
            EventKey eventKey,
            Guid? tenantId,
            CancellationToken ct = default
        )
        {
            var mapping = EventTemplateMapping
                .CreateNew(TemplateScope.System, null, EventKeyValue, templateKey, null, priority: 0, DateTime.UtcNow)
                .Value;
            return Task.FromResult<IReadOnlyList<EventTemplateMapping>>([mapping]);
        }

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
