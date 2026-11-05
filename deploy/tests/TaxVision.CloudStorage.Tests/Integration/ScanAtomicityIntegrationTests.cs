using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.CloudStorage.Api.Messaging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
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
/// CST-3 contra el SQL Server real: si la transacción del escaneo falla después del último SaveChanges, no
/// queda cambio de estado, no sale ningún sobre y el objeto sigue en temp. Tablas de Wolverine en un
/// esquema propio que se borra al terminar.
/// </summary>
public sealed class ScanAtomicityIntegrationTests : IAsyncLifetime
{
    private const string Schema = "cst_scan_atomicity_it";
    private const string Sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _fileId = Guid.NewGuid();
    private readonly CloudStorageOptions _options = new();
    private readonly FakeObjectStorage _storage = new() { DownloadContent = [0x25, 0x50, 0x44, 0x46] };
    private string _objectKey = default!;

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("taxvision-cloudstorage-development")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set CLOUDSTORAGE_DB_CONNECTION or the CloudStorage user-secrets to run this test."
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{_fileId:N}.pdf").Value;
        var file = FileObject
            .Register(
                _fileId,
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "atomicity.pdf",
                "application/pdf",
                4,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(now).IsSuccess);
        _objectKey = file.ObjectKey;
        _storage.Existing.Add((_options.TempBucket, _objectKey));

        var limit = TenantStorageLimit.Create(_tenantId, "starter", 100_000_000, 100_000_000);
        Assert.True(limit.Reserve(file.SizeBytes).IsSuccess);

        await using var db = NewContext();
        db.StorageLimits.Add(limit);
        db.Files.Add(file);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var db = NewContext())
        {
            await db.AccessLogs.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.StorageLimits.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
        }
        await DropWolverineSchemaAsync();
    }

    [Fact]
    public async Task FailureAfterSave_LeavesNoStateChangeAndNoEnvelope()
    {
        using var host = await StartHostAsync();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.MessageBus().InvokeAsync(new ScanFileCommand(_tenantId, _fileId, "it-scan-atomicity"))
        );
        await host.StopAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(FileStatus.PendingScan, await ReadStatusAsync());
        Assert.Equal(0, await CountEnvelopesAsync("wolverine_outgoing_envelopes"));
        Assert.Equal(0, await CountEnvelopesAsync("wolverine_incoming_envelopes"));
        Assert.DoesNotContain((_options.TempBucket, _objectKey), _storage.Deleted);
        Assert.Empty(_storage.Copied);
    }

    private async Task<IHost> StartHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<CloudStorageDbContext>(o =>
                    o.UseSqlServer(_connectionString).AddInterceptors(new FailAfterInfectedSave())
                );
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CloudStorageDbContext>());
                services.AddScoped<IFileObjectRepository, FileObjectRepository>();
                services.AddScoped<IStorageLimitRepository, StorageLimitRepository>();
                services.AddScoped<IStorageAuditRepository, StorageAuditRepository>();
                services.AddSingleton<IObjectStorage>(_storage);
                services.AddSingleton<IVirusScanner, InfectedVirusScanner>();
                services.AddSingleton<IContentScanner, CleanContentScanner>();
                services.AddSingleton<IFileContentInspector>(new PdfInspector());
                services.AddSingleton<IUploadPolicyResolver, FakeUploadPolicyResolver>();
                services.AddSingleton<ISystemClock, SystemClock>();
                services.Configure<CloudStorageOptions>(_ => { });
            })
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ScanFileHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_connectionString, Schema);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CloudStorageDbContext>();
                options.Policies.AutoApplyTransactions();
                options.ConfigureScanQueue();
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private async Task<FileStatus> ReadStatusAsync()
    {
        await using var db = NewContext();
        return await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.TenantId == _tenantId && f.Id == _fileId)
            .Select(f => f.Status)
            .SingleAsync();
    }

    private async Task<int> CountEnvelopesAsync(string table)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID(N'[{Schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{Schema}].[{table}]";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // Borra solo lo que Wolverine creó en el esquema de la prueba.
    private async Task DropWolverineSchemaAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
                + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
              FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id = fk.parent_object_id
              JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.procedures o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
              WHERE s.name = @schema AND o.type IN ('FN', 'IF', 'TF');
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.tables o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
              FROM sys.sequences o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = @schema;
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.types t JOIN sys.schemas s ON s.schema_id = t.schema_id
              WHERE s.name = @schema AND t.is_user_defined = 1;
            IF SCHEMA_ID(@schema) IS NOT NULL SELECT @sql += N'DROP SCHEMA ' + QUOTENAME(@schema) + N';';
            EXEC sp_executesql @sql;
            """;
        command.Parameters.AddWithValue("@schema", Schema);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Deja guardar el estado Infected y lanza justo después: la transacción del handler se revierte.</summary>
    private sealed class FailAfterInfectedSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var infected = eventData
                .Context?.ChangeTracker.Entries<FileObject>()
                .Any(e => e.Entity.Status == FileStatus.Infected);
            if (infected == true)
                throw new InvalidOperationException("Simulated failure after SaveChanges.");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class InfectedVirusScanner : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct) =>
            Task.FromResult(VirusScanResult.Infected("Eicar-Test-Signature"));
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
