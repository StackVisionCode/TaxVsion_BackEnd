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
/// El layout decide de quién es el logo. <c>tenant-base</c> es la cáscara de la oficina, así que su
/// header lleva el logo de la oficina — aunque la plantilla sea de scope System, que es justo el caso
/// de <c>billing.invoice_sent</c>: una sola plantilla sembrada sirve a todas las oficinas.
///
/// <para>Antes el logo salía de <c>request.LogoScope</c>, que los consumers de Notification nunca
/// setean: el cliente de la oficina recibía la factura con el logo de la plataforma.</para>
/// </summary>
public sealed class TenantLayoutLogoOwnerTests
{
    private static readonly EventKey EventKeyValue = EventKey.Create("billing.invoice_sent.v1").Value;
    private static readonly TemplateKey TemplateKeyValue = TemplateKey.Create("billing.invoice_sent").Value;

    private static readonly LogoAsset Logo = new(Guid.NewGuid(), "image/png", 512, IsFallback: false);

    // Compartida para poder sembrar el nombre de la oficina desde los tests.
    private static readonly FakeTenantProfileRefRepository Profiles = new();

    [Theory]
    [InlineData("tenant-base", LogoScope.Tenant, true)]
    [InlineData("system-base", LogoScope.System, false)]
    public async Task RenderAsync_takes_the_logo_from_the_layout(
        string layoutKey,
        LogoScope expectedScope,
        bool expectsTenantId
    )
    {
        var tenantId = Guid.NewGuid();
        var resolver = new RecordingLogoResolver(Logo);
        var renderer = BuildRenderer(layoutKey, resolver);

        var result = await renderer.RenderAsync(
            new RenderRequest(
                EventKeyValue,
                tenantId,
                Locale: null,
                Variables: new Dictionary<string, object?> { ["invoice_number"] = "INV-1" }
            )
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedScope, resolver.LastScope);
        Assert.Equal(expectsTenantId ? tenantId : null, resolver.LastTenantId);
    }

    [Theory]
    [InlineData("tenant-base", DispatchScopes.TenantPreferred)]
    [InlineData("system-base", DispatchScopes.System)]
    public async Task The_layout_also_decides_the_dispatch_lane(string layoutKey, string expectedScope)
    {
        // Misma regla que el logo: tenant-base es la cascara de la oficina, asi que sale por su buzon.
        var renderer = BuildRenderer(layoutKey, new RecordingLogoResolver(Logo));

        var result = await renderer.RenderAsync(
            new RenderRequest(EventKeyValue, Guid.NewGuid(), Locale: null, new Dictionary<string, object?>())
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedScope, result.Value.DispatchScope);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task A_tenant_layout_only_attaches_a_logo_it_will_reference(bool isFallback, int expectedAssets)
    {
        // Sin logo propio (o con SVG) tenant-base pinta el nombre en texto y nunca usa el cid:. Adjuntarlo
        // igual deja una parte huerfana que algunos clientes muestran como adjunto.
        var renderer = BuildRenderer("tenant-base", new RecordingLogoResolver(Logo with { IsFallback = isFallback }));

        var result = await renderer.RenderAsync(
            new RenderRequest(EventKeyValue, Guid.NewGuid(), Locale: null, Variables: new Dictionary<string, object?>())
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedAssets, result.Value.InlineAssets.Count);
    }

    [Fact]
    public async Task A_PlatformAdmin_override_beats_the_layout()
    {
        // La puerta de escape de F5: para lo que el layout no puede saber.
        var renderer = BuildRenderer(
            "system-base",
            new RecordingLogoResolver(Logo),
            dispatchOverride: DispatchScopes.TenantPreferred
        );

        var result = await renderer.RenderAsync(
            new RenderRequest(EventKeyValue, Guid.NewGuid(), Locale: null, new Dictionary<string, object?>())
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DispatchScopes.TenantPreferred, result.Value.DispatchScope);
    }

    private static FluidTemplateRenderer BuildRenderer(
        string layoutKey,
        ILogoResolver logoResolver,
        string? dispatchOverride = null
    )
    {
        var htmlFileId = Guid.NewGuid();
        var layoutHtmlFileId = Guid.NewGuid();

        // Scope System y TenantId null: la plantilla de la factura es una sola para toda la plataforma.
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
                "Invoice {{ invoice_number }}",
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
        if (dispatchOverride is not null)
            template.OverrideDispatchScope(dispatchOverride, Guid.NewGuid(), DateTime.UtcNow);

        var cloudStorage = new FakeCloudStorageClient();
        cloudStorage.Seed(htmlFileId, "<p>Invoice {{ invoice_number }}.</p>");
        cloudStorage.Seed(layoutHtmlFileId, "<html><body>{{ body | raw }}</body></html>");

        return new FluidTemplateRenderer(
            new EventTemplateResolver(new SingleMappingRepository(TemplateKeyValue)),
            new FakeEmailTemplateRepository(template),
            new FakeEmailLayoutRepository(layout),
            cloudStorage,
            logoResolver,
            Profiles,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeTemplateSourceCache(),
            NullLogger<FluidTemplateRenderer>.Instance
        );
    }

    private sealed class RecordingLogoResolver(LogoAsset asset) : ILogoResolver
    {
        public LogoScope? LastScope { get; private set; }
        public Guid? LastTenantId { get; private set; }

        public Task<LogoAsset> ResolveAsync(LogoScope logoScope, Guid? tenantId, CancellationToken ct = default)
        {
            LastScope = logoScope;
            LastTenantId = tenantId;
            return Task.FromResult(asset);
        }
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
