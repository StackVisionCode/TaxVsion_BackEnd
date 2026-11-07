using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Documents.Api.Messaging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using TaxVision.Documents.Infrastructure.Persistence;
using TaxVision.Documents.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.Documents.Tests.Integration;

/// <summary>
/// Contra el SQL Server real: host mínimo de Wolverine con la cola de generaciones de Program.cs. El
/// ProcessInvoiceGenerationCommand tiene que sobrevivir a un reinicio del host. Las tablas de Wolverine
/// van en un esquema propio; se borran al terminar junto con las generaciones del tenant de la prueba.
/// </summary>
public sealed class GenerationQueueDurabilityIntegrationTests : IAsyncLifetime
{
    private const string Schema = "doc_generation_durability_it";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _invoiceId = Guid.NewGuid();

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("DOCUMENTS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-documents00001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set DOCUMENTS_DB_CONNECTION or the Documents user-secrets to run this test."
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private DocumentsDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (var db = NewContext())
            await db
                .DocumentGenerations.IgnoreQueryFilters()
                .Where(g => g.TenantId == _tenantId)
                .ExecuteDeleteAsync();
        await DropWolverineSchemaAsync();
    }

    [Fact]
    public async Task ProcessCommand_SurvivesHostRestart()
    {
        var blocking = new GateRenderer();
        using (var first = await StartHostAsync(blocking))
        {
            var accepted = await first
                .MessageBus()
                .InvokeAsync<Result<GenerateInvoiceDocumentResult>>(NewCommand());
            Assert.True(accepted.IsSuccess);
            // El handler ya tomó el comando y espera al render: se apaga el host a mitad.
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await first.StopAsync(TimeSpan.FromSeconds(10));
        }

        Assert.True(await CountIncomingEnvelopesAsync() > 0, "The process command was not persisted while the host was down.");
        Assert.Equal(DocumentGenerationStatus.Requested, await ReadStatusAsync());

        var released = new GateRenderer();
        released.Open();
        using var second = await StartHostAsync(released);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (await ReadStatusAsync() != DocumentGenerationStatus.Uploading && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        await second.StopAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(DocumentGenerationStatus.Uploading, await ReadStatusAsync());
    }

    private GenerateInvoiceDocumentCommand NewCommand() =>
        new(
            TenantId: _tenantId,
            InvoiceId: _invoiceId,
            InvoiceNumber: "INV-IT-0001",
            DocumentVersion: 1,
            TemplateKey: "billing.invoice.v1",
            TemplateVersion: 1,
            TaxYear: 2026,
            SourceService: "billing",
            IdempotencyKey: $"invoice-pdf:{_invoiceId:N}:v1:it",
            CorrelationId: "it-generation-durability",
            Invoice: new InvoicePayload(
                "USD",
                new DateOnly(2026, 11, 3),
                null,
                new InvoiceParty("Office", "12-3456789", null),
                new InvoiceParty("Client", "", null),
                [new InvoiceLine("Tax prep", 1, 350m, 350m)],
                350m,
                0m,
                350m,
                null
            )
        );

    private async Task<IHost> StartHostAsync(IDocumentTemplateRenderer renderer)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<DocumentsDbContext>(o => o.UseSqlServer(_connectionString));
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DocumentsDbContext>());
                services.AddScoped<IDocumentGenerationRepository, DocumentGenerationRepository>();
                services.AddSingleton<IDocumentBrandingRepository, NoBranding>();
                services.AddSingleton<ITenantLogoResolver, NoLogo>();
                services.AddSingleton(renderer);
                services.AddSingleton<IHtmlToPdfConverter, FakePdf>();
                services.AddSingleton<IDocumentStorageClient, AcceptingStorage>();
                services.AddSingleton<IQrCodeGenerator, NoQr>();
                services.AddScoped<ICorrelationContext, CorrelationContext>();
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton(TestPlatform.Settings);
            })
            .UseWolverine(options =>
            {
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(GenerateInvoiceDocumentHandler))
                    .IncludeType(typeof(ProcessInvoiceGenerationHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_connectionString, Schema);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, DocumentsDbContext>();
                options.Policies.AutoApplyTransactions();
                options.ConfigureGenerationQueue();
                options.ApplyStandardFailurePolicies();
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private async Task<DocumentGenerationStatus> ReadStatusAsync()
    {
        await using var db = NewContext();
        return await db
            .DocumentGenerations.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(g => g.TenantId == _tenantId)
            .Select(g => g.Status)
            .SingleAsync();
    }

    private async Task<int> CountIncomingEnvelopesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID(N'[{Schema}].[wolverine_incoming_envelopes]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{Schema}].[wolverine_incoming_envelopes]";
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

    /// <summary>Render que avisa cuando lo llaman y espera a que lo abran (o a que cancelen).</summary>
    private sealed class GateRenderer : IDocumentTemplateRenderer
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _open.TrySetResult();

        public async Task<Result<string>> RenderHtmlAsync(
            string templateKey,
            int templateVersion,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> data,
            CancellationToken ct = default
        )
        {
            Entered.TrySetResult();
            await _open.Task.WaitAsync(ct);
            return Result.Success("<html></html>");
        }
    }

    private sealed class FakePdf : IHtmlToPdfConverter
    {
        public Task<Result<byte[]>> ConvertAsync(string html, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
    }

    private sealed class AcceptingStorage : IDocumentStorageClient
    {
        public Task<Result<StorageUploadResult>> UploadAsync(
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success(new StorageUploadResult(fileId, "temp", $"documents/{fileId:N}", 4, contentType))
            );
    }

    private sealed class NoBranding : IDocumentBrandingRepository
    {
        public Task<DocumentBranding?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<DocumentBranding?>(null);

        public Task AddAsync(DocumentBranding branding, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoLogo : ITenantLogoResolver
    {
        public Task<string?> ResolveLogoDataUriAsync(
            Guid tenantId,
            LogoAudience audience,
            CancellationToken ct = default
        ) => Task.FromResult<string?>(null);
    }

    private sealed class NoQr : IQrCodeGenerator
    {
        public string CreatePngDataUri(string content, int pixelsPerModule = 6) => string.Empty;
    }
}
