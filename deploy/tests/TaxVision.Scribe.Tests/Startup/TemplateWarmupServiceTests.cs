using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Rendering;
using TaxVision.Scribe.Infrastructure.Startup;
using TaxVision.Scribe.Tests.Rendering;

namespace TaxVision.Scribe.Tests.Startup;

/// <summary>Fase 6, plan §36 ítem 1 — al arrancar, toda EmailTemplateVersion Published queda en L1+L2 sin que el primer render real pague el round-trip a CloudStorage.</summary>
public sealed class TemplateWarmupServiceTests
{
    private static readonly TemplateKey TestTemplateKey = TemplateKey.Create("auth.password_reset").Value;
    private static readonly LayoutKey TestLayoutKey = LayoutKey.Create("system-base").Value;

    private static (
        EmailTemplate Template,
        EmailLayout Layout,
        Guid HtmlFileId,
        Guid LayoutHtmlFileId
    ) BuildPublishedTemplate()
    {
        var template = EmailTemplate
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

        return (template, layout, htmlFileId, layoutHtmlFileId);
    }

    [Fact]
    public async Task StartAsync_warms_l1_so_a_subsequent_render_never_touches_cloud_storage()
    {
        var (template, layout, htmlFileId, layoutHtmlFileId) = BuildPublishedTemplate();
        var cloudStorage = new FakeCloudStorageClient();
        cloudStorage.Seed(htmlFileId, "<p>Hi {{ name }}</p>");
        cloudStorage.Seed(layoutHtmlFileId, "<html><body>{{ body | raw }}</body></html>");

        var l1Cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var services = new ServiceCollection();
        services.AddSingleton<IEmailTemplateRepository>(new FakeEmailTemplateRepository(template));
        services.AddSingleton<IEmailLayoutRepository>(new FakeEmailLayoutRepository(layout));
        services.AddSingleton<TaxVision.Scribe.Application.Abstractions.ICloudStorageClient>(cloudStorage);
        services.AddSingleton<ILogoResolver>(
            new FakeLogoResolver(new LogoAsset(Guid.NewGuid(), "image/png", 1024, false))
        );
        services.AddSingleton<IMemoryCache>(l1Cache);
        services.AddSingleton<ITemplateSourceCache, FakeTemplateSourceCache>();
        services.AddSingleton<EventTemplateResolver>(sp => new EventTemplateResolver(
            new NoOpEventTemplateMappingRepository()
        ));
        services.AddLogging();
        services.AddSingleton<IEmailRenderer, FluidTemplateRenderer>();

        await using var provider = services.BuildServiceProvider();
        var warmup = new TemplateWarmupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TemplateWarmupService>.Instance
        );

        await warmup.StartAsync(CancellationToken.None);

        Assert.Equal(2, cloudStorage.DownloadCount);

        var renderer = provider.GetRequiredService<IEmailRenderer>();
        var previewResult = await renderer.PreviewAsync(
            template.Versions[0].Id,
            new Dictionary<string, object?> { ["name"] = "Ana" }
        );

        Assert.True(previewResult.IsSuccess);
        Assert.Equal(2, cloudStorage.DownloadCount);
    }

    private sealed class NoOpEventTemplateMappingRepository : IEventTemplateMappingRepository
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
        ) => Task.FromResult<IReadOnlyList<EventTemplateMapping>>([]);

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
