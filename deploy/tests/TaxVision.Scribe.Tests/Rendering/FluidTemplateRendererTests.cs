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

public sealed class FluidTemplateRendererTests
{
    private static readonly EventKey TestEventKey = EventKey.Create("auth.password_reset_requested.v1").Value;
    private static readonly TemplateKey TestTemplateKey = TemplateKey.Create("auth.password_reset").Value;
    private static readonly LayoutKey TestLayoutKey = LayoutKey.Create("system-base").Value;

    private readonly Guid _htmlFileId = Guid.NewGuid();
    private readonly Guid _textFileId = Guid.NewGuid();
    private readonly Guid _layoutHtmlFileId = Guid.NewGuid();

    private readonly EmailTemplate _template;
    private readonly EmailLayout _layout;
    private readonly FakeCloudStorageClient _cloudStorage = new();
    private readonly FakeTemplateSourceCache _l2Cache = new();

    public FluidTemplateRendererTests()
    {
        _template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TestTemplateKey,
                "Password reset",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        _layout = EmailLayout
            .CreateNew(TemplateScope.System, null, TestLayoutKey, "System base", null, Guid.NewGuid(), DateTime.UtcNow)
            .Value;

        var layoutVersion = _layout
            .AddDraftVersion(
                "system/layouts/system-base/v1/layout.html",
                _layoutHtmlFileId,
                null,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        _layout.PublishVersion(layoutVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        var templateVersion = _template
            .AddDraftVersion(
                "Reset your password, {{ name }}",
                "system/templates/auth.password_reset/v1/template.html",
                _htmlFileId,
                "system/templates/auth.password_reset/v1/template.txt",
                _textFileId,
                null,
                null,
                null,
                null,
                _layout.Id,
                layoutVersion.VersionNumber,
                [],
                DateTime.UtcNow
            )
            .Value;
        _template.PublishVersion(templateVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        _cloudStorage.Seed(_htmlFileId, "<p>Hi {{ name }}, click {{ link }}.</p>");
        _cloudStorage.Seed(_textFileId, "Hi {{ name }}, click {{ link }}.");
        _cloudStorage.Seed(
            _layoutHtmlFileId,
            "<html><body>{{ body | raw }}<footer>{{ current_year }}</footer></body></html>"
        );
    }

    private static readonly LogoAsset SystemLogo = new(Guid.NewGuid(), "image/png", 1024, IsFallback: false);

    private FluidTemplateRenderer BuildRenderer(IMemoryCache l1Cache) =>
        new(
            new EventTemplateResolver(new FakeEventTemplateMappingRepository(TestTemplateKey)),
            new FakeEmailTemplateRepository(_template),
            new FakeEmailLayoutRepository(_layout),
            _cloudStorage,
            new FakeLogoResolver(SystemLogo),
            l1Cache,
            _l2Cache,
            NullLogger<FluidTemplateRenderer>.Instance
        );

    private static RenderRequest BuildRequest() =>
        new(
            TestEventKey,
            TenantId: null,
            Locale: null,
            Variables: new Dictionary<string, object?> { ["name"] = "Ana", ["link"] = "https://x" }
        );

    [Fact]
    public async Task RenderAsync_parses_and_renders_variables_into_subject_html_and_text()
    {
        var renderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("Reset your password, Ana", result.Value.Subject);
        Assert.Contains("Hi Ana, click https://x.", result.Value.Html);
        Assert.Equal("Hi Ana, click https://x.", result.Value.Text);
    }

    [Fact]
    public async Task RenderAsync_wraps_the_rendered_body_inside_the_layout_placeholder()
    {
        var renderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        Assert.StartsWith("<html><body><p>Hi Ana", result.Value.Html);
        Assert.EndsWith($"<footer>{DateTime.UtcNow.Year}</footer></body></html>", result.Value.Html);
    }

    [Fact]
    public async Task RenderAsync_does_not_double_escape_the_body_html_and_still_escapes_other_layout_variables()
    {
        _cloudStorage.Seed(_layoutHtmlFileId, "<html><body>{{ body | raw }}<p>{{ tenant_name }}</p></body></html>");
        var renderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));
        var request = new RenderRequest(
            TestEventKey,
            TenantId: null,
            Locale: null,
            Variables: new Dictionary<string, object?>
            {
                ["name"] = "Ana",
                ["link"] = "https://x",
                ["tenant_name"] = "Acme & Co",
            }
        );

        var result = await renderer.RenderAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Contains("<p>Hi Ana, click https://x.</p>", result.Value.Html);
        Assert.Contains("Acme &amp; Co", result.Value.Html);
    }

    [Fact]
    public async Task RenderAsync_fails_when_layout_html_has_no_body_placeholder()
    {
        _cloudStorage.Seed(_layoutHtmlFileId, "<html><body>no placeholder here</body></html>");
        var renderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsFailure);
        Assert.Equal("EmailLayout.Body", result.Error.Code);
    }

    [Fact]
    public async Task RenderAsync_uses_l1_cache_on_a_second_call_without_hitting_cloud_storage_again()
    {
        var l1Cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var renderer = BuildRenderer(l1Cache);

        var first = await renderer.RenderAsync(BuildRequest());
        Assert.True(first.IsSuccess);
        var downloadsAfterFirstCall = _cloudStorage.DownloadCount;
        Assert.True(downloadsAfterFirstCall > 0);

        var second = await renderer.RenderAsync(BuildRequest());

        Assert.True(second.IsSuccess);
        Assert.Equal(downloadsAfterFirstCall, _cloudStorage.DownloadCount);
    }

    [Fact]
    public async Task RenderAsync_uses_l2_cache_when_l1_is_empty_avoiding_a_new_cloud_storage_download()
    {
        var firstRenderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));
        var first = await firstRenderer.RenderAsync(BuildRequest());
        Assert.True(first.IsSuccess);
        var downloadsAfterFirstCall = _cloudStorage.DownloadCount;
        Assert.True(_l2Cache.SetCalls > 0);

        // Nuevo L1 (vacío) pero mismo L2 (ya poblado por la llamada anterior) y mismo CloudStorage.
        var secondRenderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));
        var second = await secondRenderer.RenderAsync(BuildRequest());

        Assert.True(second.IsSuccess);
        Assert.Equal(downloadsAfterFirstCall, _cloudStorage.DownloadCount);
    }

    [Fact]
    public async Task RenderAsync_downloads_from_cloud_storage_on_a_full_cache_miss_and_populates_l2()
    {
        var renderer = BuildRenderer(new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        // 3 artefactos distintos en CloudStorage: html del template, text del template, html del layout.
        Assert.Equal(3, _cloudStorage.DownloadCount);
        Assert.Equal(3, _l2Cache.SetCalls);
    }

    [Fact]
    public async Task RenderAsync_fails_when_no_event_mapping_exists()
    {
        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new FakeEventTemplateMappingRepository(templateKey: null)),
            new FakeEmailTemplateRepository(_template),
            new FakeEmailLayoutRepository(_layout),
            _cloudStorage,
            new FakeLogoResolver(SystemLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            _l2Cache,
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsFailure);
        Assert.Equal("EmailRenderer.NoMapping", result.Error.Code);
    }

    [Fact]
    public async Task RenderAsync_populates_inline_assets_with_the_resolved_logo()
    {
        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new FakeEventTemplateMappingRepository(TestTemplateKey)),
            new FakeEmailTemplateRepository(_template),
            new FakeEmailLayoutRepository(_layout),
            _cloudStorage,
            new FakeLogoResolver(SystemLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            _l2Cache,
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        var asset = Assert.Single(result.Value.InlineAssets);
        Assert.Equal("logo-header", asset.ContentId);
        Assert.Equal(SystemLogo.CloudStorageFileId, asset.CloudStorageFileId);
    }

    [Fact]
    public async Task RenderAsync_shows_the_missing_logo_banner_when_the_layout_gates_it_and_logo_falls_back()
    {
        _cloudStorage.Seed(
            _layoutHtmlFileId,
            "<html><body>{% if tenant_logo_missing %}<b>Configura tu logo</b>{% endif %}{{ body | raw }}</body></html>"
        );
        var fallbackLogo = new LogoAsset(Guid.NewGuid(), "image/png", 512, IsFallback: true);
        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new FakeEventTemplateMappingRepository(TestTemplateKey)),
            new FakeEmailTemplateRepository(_template),
            new FakeEmailLayoutRepository(_layout),
            _cloudStorage,
            new FakeLogoResolver(fallbackLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            _l2Cache,
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        Assert.Contains("Configura tu logo", result.Value.Html);
    }

    [Fact]
    public async Task RenderAsync_hides_the_missing_logo_banner_when_the_tenant_has_its_own_logo()
    {
        _cloudStorage.Seed(
            _layoutHtmlFileId,
            "<html><body>{% if tenant_logo_missing %}<b>Configura tu logo</b>{% endif %}{{ body | raw }}</body></html>"
        );
        var ownLogo = new LogoAsset(Guid.NewGuid(), "image/png", 512, IsFallback: false);
        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new FakeEventTemplateMappingRepository(TestTemplateKey)),
            new FakeEmailTemplateRepository(_template),
            new FakeEmailLayoutRepository(_layout),
            _cloudStorage,
            new FakeLogoResolver(ownLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            _l2Cache,
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var result = await renderer.RenderAsync(BuildRequest());

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Configura tu logo", result.Value.Html);
    }

    private sealed class FakeEventTemplateMappingRepository(TemplateKey? templateKey) : IEventTemplateMappingRepository
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
            if (templateKey is null)
                return Task.FromResult<IReadOnlyList<EventTemplateMapping>>([]);

            var mapping = EventTemplateMapping
                .CreateNew(TemplateScope.System, null, TestEventKey, templateKey, null, priority: 0, DateTime.UtcNow)
                .Value;
            return Task.FromResult<IReadOnlyList<EventTemplateMapping>>([mapping]);
        }

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
