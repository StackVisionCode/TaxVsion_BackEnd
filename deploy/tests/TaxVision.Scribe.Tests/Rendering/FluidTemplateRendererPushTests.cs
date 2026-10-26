using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.EventMappings;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Rendering;

namespace TaxVision.Scribe.Tests.Rendering;

// La caché del push es del dueño de la plantilla: el override de un tenant nunca se sirve a otro.
public sealed class FluidTemplateRendererPushTests
{
    private static readonly EventKey PushEventKey = EventKey.Create("task.form_request_assigned.push.v1").Value;
    private static readonly TemplateKey PushTemplateKey = TemplateKey
        .Create("task.form_request_assigned.push.v1")
        .Value;

    private readonly Guid _overrideTenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly RecordingCloudStorage _storage = new();
    private readonly FakeTemplateSourceCache _l2 = new();
    private readonly MemoryCache _l1 = new(new MemoryCacheOptions { SizeLimit = 1000 });
    private readonly EmailTemplate _system;
    private readonly EmailTemplate _override;

    public FluidTemplateRendererPushTests()
    {
        _system = PushTemplate(TemplateScope.System, null, "System text");
        _override = PushTemplate(TemplateScope.Tenant, _overrideTenant, "Override text");
    }

    [Fact]
    public async Task A_tenant_override_is_not_served_to_another_tenant_from_the_cache()
    {
        var renderer = Renderer();

        var overridden = await renderer.RenderAsync(Request(_overrideTenant));
        var other = await renderer.RenderAsync(Request(_otherTenant));
        var overriddenAgain = await renderer.RenderAsync(Request(_overrideTenant));

        Assert.Equal("Override text", overridden.Value.Text);
        Assert.Equal("System text", other.Value.Text);
        Assert.Equal("Override text", overriddenAgain.Value.Text);
    }

    [Fact]
    public async Task The_override_text_is_downloaded_with_the_owner_tenant_token()
    {
        await Renderer().RenderAsync(Request(_overrideTenant));
        await Renderer().RenderAsync(Request(_otherTenant));

        Assert.Equal(
            [
                (_override.Versions.Single().TextFileId!.Value, (Guid?)_overrideTenant),
                (_system.Versions.Single().TextFileId!.Value, PlatformTenant.Id),
            ],
            _storage.Downloads
        );
    }

    [Fact]
    public async Task A_tenant_override_of_another_channel_falls_back_to_the_system_push_template()
    {
        var emailOverride = EmailTemplate
            .CreateNew(
                TemplateScope.Tenant,
                _overrideTenant,
                PushTemplateKey,
                "Email with the same key",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        var result = await Renderer(emailOverride).RenderAsync(Request(_overrideTenant));

        Assert.True(result.IsSuccess);
        Assert.Equal("System text", result.Value.Text);
    }

    private EmailTemplate PushTemplate(TemplateScope scope, Guid? tenantId, string text)
    {
        var template = EmailTemplate
            .CreateNew(
                scope,
                tenantId,
                PushTemplateKey,
                "Assigned push",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                TemplateChannel.Push
            )
            .Value;
        var textFileId = Guid.NewGuid();
        _storage.Seed(textFileId, text);
        var version = template.AddPushDraftVersion("Title", "push/text.txt", textFileId, [], DateTime.UtcNow).Value;
        template.PublishVersion(version.Id, Guid.NewGuid(), DateTime.UtcNow);
        return template;
    }

    private FluidTemplateRenderer Renderer(EmailTemplate? tenantOverride = null) =>
        new(
            new EventTemplateResolver(new SingleMapping()),
            new TenantAwareTemplates(_system, tenantOverride ?? _override),
            new FakeEmailLayoutRepository(
                EmailLayout
                    .CreateNew(
                        TemplateScope.System,
                        null,
                        LayoutKey.Create("system-base").Value,
                        "Base",
                        null,
                        Guid.NewGuid(),
                        DateTime.UtcNow
                    )
                    .Value
            ),
            _storage,
            new FakeLogoResolver(new LogoAsset(Guid.NewGuid(), "image/png", 1, IsFallback: false)),
            new FakeTenantProfileRefRepository(),
            TestPlatform.Settings,
            _l1,
            _l2,
            NullLogger<FluidTemplateRenderer>.Instance
        );

    private static RenderRequest Request(Guid tenantId) =>
        new(
            PushEventKey,
            tenantId,
            null,
            new Dictionary<string, object?> { ["language"] = "En" },
            Channel: TemplateChannel.Push
        );

    private sealed class SingleMapping : IEventTemplateMappingRepository
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
        ) =>
            Task.FromResult<IReadOnlyList<EventTemplateMapping>>([
                EventTemplateMapping
                    .CreateNew(
                        TemplateScope.System,
                        null,
                        PushEventKey,
                        PushTemplateKey,
                        null,
                        priority: 0,
                        DateTime.UtcNow
                    )
                    .Value,
            ]);

        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    }

    // Igual que el repositorio real: el override del tenant gana sobre la plantilla del sistema.
    private sealed class TenantAwareTemplates(EmailTemplate system, EmailTemplate tenantOverride)
        : IEmailTemplateRepository
    {
        public Task<Result<EmailTemplate>> GetByKeyAsync(
            TemplateKey templateKey,
            Guid? tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(tenantId == tenantOverride.TenantId ? tenantOverride : system));

        public Task<Result<EmailTemplate>> GetByIdAsync(Guid templateId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<(EmailTemplate Template, EmailTemplateVersion Version)>> GetVersionByIdAsync(
            Guid versionId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task AddAsync(EmailTemplate value, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<(EmailTemplate Template, EmailTemplateVersion Version)>> GetAllPublishedAsync(
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<EmailTemplate>> GetWithArchivedVersionsOlderThanAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class RecordingCloudStorage : ICloudStorageClient
    {
        private readonly Dictionary<Guid, string> _sources = [];

        public List<(Guid FileId, Guid? TenantId)> Downloads { get; } = [];

        public void Seed(Guid fileId, string source) => _sources[fileId] = source;

        public Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default)
        {
            Downloads.Add((fileId, tenantId));
            return Task.FromResult(Result.Success(_sources[fileId]));
        }

        public Task<Result<string>> UploadAsync(
            Guid? tenantId,
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            string folderType,
            Guid actorId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }
}
