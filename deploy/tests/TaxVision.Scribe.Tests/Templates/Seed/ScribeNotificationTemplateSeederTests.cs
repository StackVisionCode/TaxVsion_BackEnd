using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Scribe.Application.Templates.BaseLayouts;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Seed;
using TaxVision.Scribe.Tests.Templates.Validation;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// El seeder corre al arrancar: una plantilla nueva queda publicada con su mapping, y una ya guardada con
/// una versión de contenido menor se republica. Contra un ScribeDbContext InMemory y almacenamiento falso.
/// </summary>
public sealed class ScribeNotificationTemplateSeederTests
{
    private const string WispKey = "tenant.wisp_review_due.v1";

    [Fact]
    public async Task Al_arrancar_publica_la_plantilla_del_WISP_y_su_mapping()
    {
        await using var host = await SeedHost.StartAsync();

        var template = await host.WaitForPublishedAsync(WispKey, minimumVersion: 23);

        var published = template.Versions.Single(v => v.Status == EmailVersionStatus.Published);
        Assert.Equal(23, published.SeedContentVersion);
        Assert.Contains("FTC Safeguards Rule", host.Storage.Read(published.HtmlFileId!.Value));
        Assert.True(await host.HasMappingAsync(WispKey));
    }

    [Fact]
    public async Task Una_version_guardada_menor_se_republica_con_la_nueva()
    {
        await using var host = await SeedHost.StartAsync(existingWispVersion: 22);

        var template = await host.WaitForPublishedAsync(WispKey, minimumVersion: 23);

        Assert.Equal(23, template.Versions.Single(v => v.Status == EmailVersionStatus.Published).SeedContentVersion);
        Assert.Contains(template.Versions, v => v.Status == EmailVersionStatus.Archived && v.SeedContentVersion == 22);
    }

    // SCR-02: una plantilla que falla no se lleva a las demás.
    [Fact]
    public async Task One_failing_template_does_not_stop_the_rest()
    {
        await using var host = await SeedHost.StartAsync(throwOnUpload: 3);

        var published = await host.WaitForPublishedCountAsync(NotificationTemplateSeedSource.All.Count - 1);

        Assert.Equal(NotificationTemplateSeedSource.All.Count - 1, published);
    }

    private sealed class SeedHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly FakeLifetime _lifetime = new();

        private SeedHost(ServiceProvider provider, RecordingStorage storage)
        {
            _provider = provider;
            Storage = storage;
        }

        public RecordingStorage Storage { get; }

        public static async Task<SeedHost> StartAsync(int? existingWispVersion = null, int? throwOnUpload = null)
        {
            var storage = new RecordingStorage();
            var databaseName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddSingleton<ITenantContext>(new NoTenantContext());
            services.AddDbContext<ScribeDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ScribeDbContext>());
            services.AddSingleton<ITemplateStorageService>(storage);
            var host = new SeedHost(services.BuildServiceProvider(), storage);

            await host.SeedLayoutsAndExistingAsync(existingWispVersion);
            storage.ThrowOnUpload = throwOnUpload;

            var seeder = new ScribeNotificationTemplateSeeder(
                host._provider.GetRequiredService<IServiceScopeFactory>(),
                host._lifetime,
                new FreeSeedLock(),
                NullLogger<ScribeNotificationTemplateSeeder>.Instance
            );
            await seeder.StartAsync(CancellationToken.None);
            host._lifetime.Start();
            return host;
        }

        public async Task<EmailTemplate> WaitForPublishedAsync(string templateKey, int minimumVersion)
        {
            var key = TemplateKey.Create(templateKey).Value;
            for (var attempt = 0; attempt < 300; attempt++)
            {
                await using var scope = _provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ScribeDbContext>();
                var template = await db
                    .EmailTemplates.Include(t => t.Versions)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Scope == TemplateScope.System && t.TemplateKey == key);
                var published = template?.Versions.FirstOrDefault(v => v.Status == EmailVersionStatus.Published);
                if ((published?.SeedContentVersion ?? 0) >= minimumVersion)
                    return template!;
                await Task.Delay(100);
            }
            throw new TimeoutException($"Template {templateKey} was not published at v{minimumVersion}.");
        }

        /// <summary>Espera a que el conteo llegue a <paramref name="expected"/> o deje de crecer.</summary>
        public async Task<int> WaitForPublishedCountAsync(int expected)
        {
            var last = -1;
            var unchanged = 0;
            while (unchanged < 30)
            {
                await using var scope = _provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ScribeDbContext>();
                var count = await db.EmailTemplates.CountAsync(t =>
                    t.Versions.Any(v => v.Status == EmailVersionStatus.Published)
                );
                if (count >= expected)
                    return count;
                unchanged = count == last ? unchanged + 1 : 0;
                last = count;
                await Task.Delay(100);
            }
            return last;
        }

        public async Task<bool> HasMappingAsync(string eventKey)
        {
            var key = EventKey.Create(eventKey).Value;
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ScribeDbContext>();
            return await db.EventTemplateMappings.AnyAsync(m => m.Scope == TemplateScope.System && m.EventKey == key);
        }

        private async Task SeedLayoutsAndExistingAsync(int? existingWispVersion)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ScribeDbContext>();
            var systemBase = Layout("system-base", BaseLayoutHtml.SystemBaseVersion);
            db.EmailLayouts.Add(systemBase);
            db.EmailLayouts.Add(Layout("tenant-base", BaseLayoutHtml.TenantBaseVersion));

            if (existingWispVersion is { } version)
            {
                var template = EmailTemplate
                    .CreateNew(
                        TemplateScope.System,
                        null,
                        TemplateKey.Create(WispKey).Value,
                        "WISP",
                        null,
                        PlatformTenant.Id,
                        DateTime.UtcNow
                    )
                    .Value;
                var fileId = Storage.Store("<p>old</p>");
                var draft = template
                    .AddDraftVersion(
                        "Old",
                        "scribe/html",
                        fileId,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        systemBase.Id,
                        systemBase.Versions.Single().VersionNumber,
                        [],
                        DateTime.UtcNow,
                        version
                    )
                    .Value;
                template.PublishVersion(draft.Id, PlatformTenant.Id, DateTime.UtcNow);
                db.EmailTemplates.Add(template);
            }

            await db.SaveChangesAsync();
        }

        private EmailLayout Layout(string key, int seedVersion)
        {
            var layout = EmailLayout
                .CreateNew(
                    TemplateScope.System,
                    null,
                    LayoutKey.Create(key).Value,
                    key,
                    null,
                    PlatformTenant.Id,
                    DateTime.UtcNow
                )
                .Value;
            var version = layout
                .AddDraftVersion(
                    "scribe/layout",
                    Storage.Store("{{ body | raw }}"),
                    null,
                    null,
                    null,
                    null,
                    DateTime.UtcNow,
                    seedVersion
                )
                .Value;
            layout.PublishVersion(version.Id, PlatformTenant.Id, DateTime.UtcNow);
            return layout;
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Stop();
            await _provider.DisposeAsync();
        }
    }

    /// <summary>Almacenamiento en memoria: lo subido se puede descargar de inmediato.</summary>
    private sealed class RecordingStorage : ITemplateStorageService
    {
        private readonly FakeTemplateStorageService _inner = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _texts = new();

        public Guid Store(string content)
        {
            var fileId = Guid.NewGuid();
            _texts[fileId] = content;
            _inner.Seed(fileId, content);
            return fileId;
        }

        public string Read(Guid fileId) => _texts[fileId];

        public int? ThrowOnUpload { get; set; }

        private int _uploads;

        public async Task<BuildingBlocks.Results.Result<TemplateStorageUpload>> UploadAsync(
            Guid? tenantId,
            TemplateArtifactKind kind,
            byte[] content,
            Guid actorId,
            CancellationToken ct = default
        )
        {
            if (Interlocked.Increment(ref _uploads) == ThrowOnUpload)
                throw new InvalidOperationException("Storage failed on this upload.");

            var result = await _inner.UploadAsync(tenantId, kind, content, actorId, ct);
            if (result.IsSuccess)
                _texts[result.Value.FileId] = System.Text.Encoding.UTF8.GetString(content);
            return result;
        }

        public Task<BuildingBlocks.Results.Result<string>> DownloadTextAsync(
            Guid fileId,
            Guid? tenantId,
            CancellationToken ct = default
        ) => _inner.DownloadTextAsync(fileId, tenantId, ct);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Start() => _started.Cancel();

        public void Stop() => _stopping.Cancel();

        public void StopApplication() => Stop();
    }

    private sealed class FreeSeedLock : ISeedLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable?>(new Held());

        private sealed class Held : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
