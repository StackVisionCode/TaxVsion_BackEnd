using System.Reflection;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Storage;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.CloudStorage.Tests.Infrastructure;

public sealed class FileSourceClaimReconciliationJobTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Groups_pending_files_by_tenant_in_batches_of_at_most_500()
    {
        var harness = new Harness();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var idsA = await harness.SeedAsync(tenantA, 1200);
        var idsB = await harness.SeedAsync(tenantB, 3);

        var completed = await harness.RunAsync();

        Assert.True(completed);
        var events = harness.Published.OfType<FileSourcesClaimRequestedIntegrationEvent>().ToList();
        Assert.All(
            events,
            e => Assert.InRange(e.FileIds.Count, 1, FileSourcesClaimRequestedIntegrationEvent.MaxFileIds)
        );
        var forA = events.Where(e => e.TenantId == tenantA).ToList();
        Assert.Equal([500, 500, 200], forA.Select(e => e.FileIds.Count));
        Assert.Equal(idsA.Order(), forA.SelectMany(e => e.FileIds).Order());
        var forB = Assert.Single(events, e => e.TenantId == tenantB);
        Assert.Equal(idsB.Order(), forB.FileIds.Order());
        // Una correlación por pasada.
        Assert.Single(events.Select(e => e.CorrelationId).Distinct());
        Assert.False(string.IsNullOrEmpty(events[0].CorrelationId));
    }

    [Fact]
    public async Task A_smaller_batch_size_caps_each_message()
    {
        var harness = new Harness(batchSize: 2);
        var tenant = Guid.NewGuid();
        await harness.SeedAsync(tenant, 5);

        await harness.RunAsync();

        Assert.Equal(
            [2, 2, 1],
            harness.Published.OfType<FileSourcesClaimRequestedIntegrationEvent>().Select(e => e.FileIds.Count)
        );
    }

    [Fact]
    public async Task Skips_declared_deleted_and_unavailable_files()
    {
        var harness = new Harness();
        var tenant = Guid.NewGuid();
        var pending = await harness.SeedAsync(tenant, 1);
        await harness.SeedAsync(tenant, 1, file => file.SoftDelete(Now, TimeSpan.FromDays(30)));
        await harness.SeedAsync(
            tenant,
            1,
            file =>
                file.DeclareSource(
                    FileSourceDeclaration.Create("billing", "invoice", Guid.NewGuid(), "invoice_pdf", null).Value,
                    Now
                )
        );
        await harness.SeedAsync(tenant, 1, available: false);

        await harness.RunAsync();

        var claim = Assert.Single(harness.Published.OfType<FileSourcesClaimRequestedIntegrationEvent>());
        Assert.Equal(pending, claim.FileIds);
    }

    [Fact]
    public async Task Without_pending_files_nothing_is_published()
    {
        var harness = new Harness();

        var completed = await harness.RunAsync();

        Assert.True(completed);
        Assert.Empty(harness.Published);
    }

    [Fact]
    public async Task A_failed_batch_returns_false()
    {
        var harness = new Harness(registerOutbox: false);
        await harness.SeedAsync(Guid.NewGuid(), 1);

        Assert.False(await harness.RunAsync());
    }

    [Fact]
    public async Task A_failed_query_returns_false()
    {
        var harness = new Harness(registerRepository: false);

        Assert.False(await harness.RunAsync());
    }

    private sealed class Harness
    {
        private readonly ServiceProvider _provider;
        private readonly int _batchSize;

        public Harness(int batchSize = 500, bool registerOutbox = true, bool registerRepository = true)
        {
            _batchSize = batchSize;
            var databaseName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddSingleton<ITenantContext>(new NoTenant());
            services.AddDbContext<CloudStorageDbContext>(options =>
                options
                    .UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            );
            if (registerOutbox)
                services.AddScoped(sp =>
                    RecordingCloudStorageOutbox.Create(sp.GetRequiredService<CloudStorageDbContext>(), Published)
                );
            if (registerRepository)
                services.AddScoped<IFileObjectRepository, FileObjectRepository>();
            _provider = services.BuildServiceProvider();
        }

        public List<object> Published { get; } = [];

        public async Task<List<Guid>> SeedAsync(
            Guid tenantId,
            int count,
            Action<FileObject>? change = null,
            bool available = true
        )
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CloudStorageDbContext>();
            var ids = new List<Guid>();
            for (var i = 0; i < count; i++)
            {
                var file = NewFile(tenantId, available);
                change?.Invoke(file);
                file.ClearDomainEvents();
                db.Files.Add(file);
                ids.Add(file.Id);
            }
            await db.SaveChangesAsync();
            return ids;
        }

        public Task<bool> RunAsync()
        {
            var job = new FileSourceClaimReconciliationJob(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                new StartedLifetime(),
                Options.Create(new FileSourceClaimOptions { BatchSize = _batchSize }),
                NullLogger<FileSourceClaimReconciliationJob>.Instance
            );
            return job.RunOnceSafeAsync(CancellationToken.None);
        }

        private static FileObject NewFile(Guid tenantId, bool available)
        {
            var id = Guid.NewGuid();
            var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2026/{id:N}.pdf").Value;
            var file = FileObject
                .Register(
                    id,
                    tenantId,
                    OwnerType.Tenant,
                    null,
                    FolderType.Documents,
                    2026,
                    key,
                    "file.pdf",
                    "application/pdf",
                    10,
                    Guid.NewGuid(),
                    Now,
                    Now.AddHours(1)
                )
                .Value;
            if (!available)
                return file;
            file.MarkPendingScan(Now);
            file.MarkScanning();
            file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", Now);
            return file;
        }
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }
}

/// <summary>Outbox de prueba: lo publicado solo llega a la lista si la unidad confirma.</summary>
public class RecordingCloudStorageOutbox : DispatchProxy
{
    private readonly List<object> _pending = [];
    private CloudStorageDbContext? _context;
    private List<object> _flushed = [];

    internal static IDbContextOutbox<CloudStorageDbContext> Create(CloudStorageDbContext context, List<object> flushed)
    {
        var outbox = Create<IDbContextOutbox<CloudStorageDbContext>, RecordingCloudStorageOutbox>();
        var recorder = (RecordingCloudStorageOutbox)(object)outbox;
        recorder._context = context;
        recorder._flushed = flushed;
        return outbox;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_DbContext" => _context,
            "PublishAsync" => Enqueue(args![0]!),
            "SaveChangesAndFlushMessagesAsync" => SaveAndFlushAsync(args!.OfType<CancellationToken>().FirstOrDefault()),
            _ => throw new NotSupportedException($"El doble de outbox no implementa {targetMethod?.Name}."),
        };

    private ValueTask Enqueue(object message)
    {
        _pending.Add(message);
        return ValueTask.CompletedTask;
    }

    private async Task SaveAndFlushAsync(CancellationToken ct)
    {
        await _context!.SaveChangesAsync(ct);
        if (_context.Database.CurrentTransaction is { } transaction)
            await transaction.CommitAsync(ct);
        _flushed.AddRange(_pending);
        _pending.Clear();
    }
}
