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

public sealed class FluidTemplateRendererPreviewTests
{
    private static readonly LogoAsset SystemLogo = new(Guid.NewGuid(), "image/png", 1024, IsFallback: false);

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

    [Fact]
    public async Task PreviewAsync_renders_a_draft_version_directly_by_id_without_an_event_mapping()
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create("auth.password_reset").Value,
                "Password reset",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.System,
                null,
                LayoutKey.Create("system-base").Value,
                "System base",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        var layoutHtmlFileId = Guid.NewGuid();
        var layoutVersion = layout
            .AddDraftVersion("layout.html", layoutHtmlFileId, null, null, null, null, DateTime.UtcNow)
            .Value;
        layout.PublishVersion(layoutVersion.Id, Guid.NewGuid(), DateTime.UtcNow);

        var htmlFileId = Guid.NewGuid();
        var version = template
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
        // Deliberately left as Draft — Preview must work on drafts, not only Published versions.
        Assert.Equal(EmailVersionStatus.Draft, version.Status);

        var cloudStorage = new FakeCloudStorageClient();
        cloudStorage.Seed(htmlFileId, "<p>Hi {{ name }}</p>");
        cloudStorage.Seed(layoutHtmlFileId, "<html><body>{{ body | raw }}</body></html>");

        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new NoOpEventTemplateMappingRepository()),
            new FakeEmailTemplateRepository(template),
            new FakeEmailLayoutRepository(layout),
            cloudStorage,
            new FakeLogoResolver(SystemLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeTemplateSourceCache(),
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var result = await renderer.PreviewAsync(version.Id, new Dictionary<string, object?> { ["name"] = "Ana" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Reset your password, Ana", result.Value.Subject);
        Assert.Equal("<html><body><p>Hi Ana</p></body></html>", result.Value.Html);
    }

    [Fact]
    public async Task PreviewAsync_fails_when_the_version_does_not_exist()
    {
        var template = EmailTemplate
            .CreateNew(
                TemplateScope.System,
                null,
                TemplateKey.Create("auth.password_reset").Value,
                "Password reset",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var layout = EmailLayout
            .CreateNew(
                TemplateScope.System,
                null,
                LayoutKey.Create("system-base").Value,
                "System base",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        var renderer = new FluidTemplateRenderer(
            new EventTemplateResolver(new NoOpEventTemplateMappingRepository()),
            new FakeEmailTemplateRepository(template),
            new FakeEmailLayoutRepository(layout),
            new FakeCloudStorageClient(),
            new FakeLogoResolver(SystemLogo),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }),
            new FakeTemplateSourceCache(),
            NullLogger<FluidTemplateRenderer>.Instance
        );

        var result = await renderer.PreviewAsync(Guid.NewGuid(), new Dictionary<string, object?>());

        Assert.True(result.IsFailure);
    }
}
