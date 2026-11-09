using System.Collections.Concurrent;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.CloudStorage.Api.Messaging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Files.DomainEvents;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Time;
using TaxVision.CloudStorage.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// La cola de escaneo de Program.cs contra SQL real, en una base temporal. Se escucha el envío real de
/// FileAvailable por una cola local: contar sobres no ve lo que sale de memoria. Un escaneo que pierde contra
/// un borrado no puede anunciar el archivo.
/// </summary>
[Collection(CloudStorageConcurrencyCollection.Name)]
public sealed class ScanConflictOutboxIntegrationTests(CloudStorageConcurrencyFixture fixture)
{
    private const string Schema = "cst_scan_conflict_it";
    private const string Sha256 = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(6);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly CloudStorageOptions _options = new();
    private readonly FakeObjectStorage _storage = new() { DownloadContent = [0x25, 0x50, 0x44, 0x46] };
    private readonly AvailableProbe _probe = new();

    [Fact]
    public async Task A_clean_scan_announces_the_file_after_the_commit()
    {
        var fileId = await SeedFileAsync();
        using var host = await StartHostAsync(new InlineVirusScanner(() => Task.CompletedTask));

        await host.MessageBus().PublishAsync(new ScanFileCommand(_tenantId, fileId, "it-scan-control"));

        Assert.True(await _probe.WaitForAsync(fileId, TimeSpan.FromSeconds(30)));
        Assert.Equal(FileStatus.Available, await ReadStatusAsync(fileId));
        await Task.Delay(SettleTime);
        Assert.Equal(["it-scan-control"], _probe.Correlations(fileId));
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    // CST-9, el camino real: comando en línea, evento de dominio por la cola durable y aviso por el outbox.
    [Fact]
    public async Task An_approved_review_announces_the_file_once_after_the_commit()
    {
        var fileId = await SeedFileAsync(pendingReview: true);
        using var host = await StartHostAsync(new InlineVirusScanner(() => Task.CompletedTask));

        var result = await host.MessageBus()
            .InvokeAsync<Result>(
                new ApproveContentReviewCommand(
                    _tenantId,
                    fileId,
                    Guid.NewGuid(),
                    new RequestAuditContext(null, null, "it-review")
                )
            );

        Assert.True(result.IsSuccess);
        Assert.True(await _probe.WaitForAsync(fileId, TimeSpan.FromSeconds(30)));
        Assert.Equal(FileStatus.Available, await ReadStatusAsync(fileId));
        await Task.Delay(SettleTime);
        Assert.Equal(["it-review"], _probe.Correlations(fileId));
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_scan_that_loses_to_a_delete_does_not_announce_the_file()
    {
        var fileId = await SeedFileAsync();
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // El borrado entra mientras el antivirus trabaja: el guardado del escaneo choca.
        using var host = await StartHostAsync(
            new InlineVirusScanner(async () =>
            {
                await SoftDeleteAsync(fileId);
                deleted.TrySetResult();
            })
        );

        await host.MessageBus().PublishAsync(new ScanFileCommand(_tenantId, fileId, "it-scan-conflict"));
        await deleted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(await _probe.WaitForAsync(fileId, SettleTime), "FileAvailable salió de un escaneo revertido.");
        Assert.Equal(FileStatus.SoftDeleted, await ReadStatusAsync(fileId));
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    // Fila L: ClamAV corre sin transacción abierta; el escaneo en curso lo sostiene un candado de sesión.
    [Fact]
    public async Task The_virus_scan_runs_without_an_open_transaction()
    {
        var fileId = await SeedFileAsync();
        (int Holders, int InTransaction) claim = (-1, -1);
        FileStatus? statusWhileScanning = null;
        using var host = await StartHostAsync(
            new InlineVirusScanner(async () =>
            {
                claim = await ScanClaimHoldersAsync(fileId);
                statusWhileScanning = await ReadStatusAsync(fileId);
            })
        );

        await host.MessageBus().PublishAsync(new ScanFileCommand(_tenantId, fileId, "it-scan-outside-tx"));

        Assert.True(await _probe.WaitForAsync(fileId, TimeSpan.FromSeconds(30)));
        Assert.Equal((1, 0), claim);
        Assert.Equal(FileStatus.Scanning, statusWhileScanning);
        Assert.Equal(FileStatus.Available, await ReadStatusAsync(fileId));
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    // Quién tiene el candado del escaneo y cuántos de esos tienen una transacción abierta.
    private async Task<(int Holders, int InTransaction)> ScanClaimHoldersAsync(Guid fileId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*), COUNT(t.session_id)
            FROM sys.dm_tran_locks l
            LEFT JOIN sys.dm_tran_session_transactions t ON t.session_id = l.request_session_id
            WHERE l.resource_type = 'APPLICATION'
              AND l.resource_database_id = DB_ID()
              AND l.request_status = 'GRANT'
              AND l.resource_description LIKE @resource
            """,
            connection
        );
        // La descripción trae el nombre del recurso recortado a 32 caracteres.
        command.Parameters.AddWithValue("@resource", $"%cloudstorage:scan:{fileId:N}"[..33] + "%");
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task<IHost> StartHostAsync(IVirusScanner scanner)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<CloudStorageDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
                services.AddSingleton<ITenantContext>(new TemporaryDatabase.FixedTenant(_tenantId));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CloudStorageDbContext>());
                services.AddScoped<IFileObjectRepository, FileObjectRepository>();
                services.AddScoped<IScanClaims, SqlScanClaims>();
                services.AddScoped<IStorageLimitRepository, StorageLimitRepository>();
                services.AddScoped<IStorageAuditRepository, StorageAuditRepository>();
                services.AddSingleton<IObjectStorage>(_storage);
                services.AddSingleton(scanner);
                services.AddSingleton<IContentScanner, CleanContentScanner>();
                services.AddSingleton<IFileContentInspector>(new PdfInspector());
                services.AddSingleton<IUploadPolicyResolver, FakeUploadPolicyResolver>();
                services.AddSingleton<ISystemClock, SystemClock>();
                services.AddSingleton(_probe);
                services.Configure<CloudStorageOptions>(_ => { });
            })
            .UseWolverine(options =>
            {
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ScanFileHandler))
                    .IncludeType(typeof(RecordScanVerdictHandler))
                    .IncludeType(typeof(ApproveContentReviewHandler))
                    .IncludeType(typeof(FileBecameAvailableDomainEventHandler))
                    .IncludeType(typeof(AvailableProbeHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(fixture.ConnectionString, Schema);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CloudStorageDbContext>();
                options.Policies.AutoApplyTransactions();
                options.UseTaxVisionDomainEvents();
                options.ConfigureScanQueue(2);
                options.PublishMessage<FileAvailableIntegrationEvent>().ToLocalQueue("cst-available-probe");
                options.UseTaxVisionConflictPolicy(
                    new ConflictRetryOptions { Delays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)] }
                );
                options.ApplyStandardFailurePolicies();
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private async Task<Guid> SeedFileAsync(bool pendingReview = false)
    {
        var fileId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{fileId:N}.pdf").Value;
        var file = FileObject
            .Register(
                fileId,
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "conflict.pdf",
                "application/pdf",
                4,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(now).IsSuccess);
        _storage.Existing.Add((_options.TempBucket, file.ObjectKey));

        var limit = TenantStorageLimit.Create(_tenantId, "starter", 100_000_000, 100_000_000);
        if (pendingReview)
        {
            Assert.True(file.MarkScanning().IsSuccess);
            var checksum = ChecksumSha256.Create(Sha256).Value;
            Assert.True(file.MarkPendingReview("Doubt", "application/pdf", checksum, now).IsSuccess);
        }
        else
            Assert.True(limit.Reserve(file.SizeBytes).IsSuccess);

        await using var db = fixture.NewContext(_tenantId);
        db.StorageLimits.Add(limit);
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return fileId;
    }

    private async Task SoftDeleteAsync(Guid fileId)
    {
        await using var db = fixture.NewContext(_tenantId);
        var file = await db.Files.SingleAsync(f => f.Id == fileId);
        Assert.True(file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<FileStatus> ReadStatusAsync(Guid fileId)
    {
        await using var db = fixture.NewContext(_tenantId);
        return await db.Files.AsNoTracking().Where(f => f.Id == fileId).Select(f => f.Status).SingleAsync();
    }

    private sealed class InlineVirusScanner(Func<Task> whileScanning) : IVirusScanner
    {
        private int _scans;

        public async Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _scans) == 1)
                await whileScanning();
            return VirusScanResult.Clean();
        }
    }

    private sealed class CleanContentScanner : IContentScanner
    {
        public Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct) =>
            Task.FromResult(ContentScanResult.Clean());
    }

    private sealed class PdfInspector : IFileContentInspector
    {
        public Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct) =>
            Task.FromResult(new InspectedContent("application/pdf", Sha256));
    }
}

public sealed class AvailableProbe
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _received = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string?>> _correlations = new();

    public void Record(Guid fileId, string? correlationId)
    {
        _correlations.GetOrAdd(fileId, _ => new()).Enqueue(correlationId);
        Slot(fileId).TrySetResult();
    }

    public IReadOnlyList<string?> Correlations(Guid fileId) =>
        _correlations.TryGetValue(fileId, out var received) ? [.. received] : [];

    public async Task<bool> WaitForAsync(Guid fileId, TimeSpan timeout)
    {
        try
        {
            await Slot(fileId).Task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private TaskCompletionSource Slot(Guid fileId) =>
        _received.GetOrAdd(fileId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public static class AvailableProbeHandler
{
    public static void Handle(FileAvailableIntegrationEvent evt, AvailableProbe probe) =>
        probe.Record(evt.FileId, evt.CorrelationId);
}
