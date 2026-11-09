using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.DomainEvents;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.RecordManualPayment;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-BB1 y BIL-BB2 contra el SQL Server local, con el BillingDbContext y los handlers reales: el pago que deja
/// la factura pagada guarda en la misma transacción el InvoicePaidDomainEvent; su handler devuelve el aviso
/// (outbox) y el GenerateInvoicePdfCommand, que queda en una cola local durable. Base temporal (BIL-14) y sobres en un
/// esquema propio que se borra al terminar; avisos a un puerto TCP cerrado.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class BillingDurableLocalQueueIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _connectionString = database.ConnectionString;

    private readonly string _schema = $"bilbb2_{Guid.NewGuid():N}"[..15];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FailAfterPaidInterceptor _interceptor = new();
    private readonly RecordingDocumentClient _documents = new();
    private Guid _invoiceId;
    private IHost _host = null!;

    internal Guid TenantId => _tenantId;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"EXEC('CREATE SCHEMA [{_schema}]')");
        var invoice = Invoice
            .CreateDraft(
                _tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-90001").Value, Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess
        );
        _invoiceId = invoice.Id;
        await using (var db = NewContext())
        {
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            _documents.Release();
            await _host.StopAsync();
            _host.Dispose();
        }
        await using (var db = NewContext())
        {
            // Cascada a pagos, líneas y rastro de estado: solo los datos de este tenant de prueba.
            await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
        }
        await DropSchemaAsync();
    }

    [Fact]
    public async Task ManualPayment_PaidEventAndPdfCommandStoredWithInvoice()
    {
        var result = await PayAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, await StatusAsync());
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "InvoicePaidDomainEvent"));

        // El handler de dominio devuelve un solo aviso (outbox) y un solo comando de PDF.
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(InvoicePaidIntegrationEvent)) == 1
            )
        );

        // Con Documents "colgado", el comando de PDF a medio procesar está guardado: un reinicio no lo pierde.
        // En una cola en memoria no habría fila hasta terminar (Wolverine solo anota el sobre ya manejado).
        Assert.True(await WaitUntilAsync(() => Task.FromResult(_documents.Calls.Count == 1)));
        Assert.Equal(
            1,
            await CountRowsAsync("wolverine_incoming_envelopes", nameof(GenerateInvoicePdfCommand), status: "Incoming")
        );
        _documents.Release();
    }

    [Fact]
    public async Task ManualPayment_FailureAfterSave_LeavesNoRowNorEnvelope()
    {
        _interceptor.Fail = true;

        await Assert.ThrowsAnyAsync<Exception>(PayAsync);

        Assert.Equal(InvoiceStatus.Issued, await StatusAsync());
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "InvoicePaidDomainEvent"));
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", nameof(GenerateInvoicePdfCommand)));
        Assert.Equal(0, await CountRowsAsync("wolverine_outgoing_envelopes", nameof(InvoicePaidIntegrationEvent)));
    }

    private Task<Result<RecordManualPaymentResult>> PayAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<RecordManualPaymentResult>>(
                new RecordManualPaymentCommand(
                    _tenantId,
                    _invoiceId,
                    PaymentMethod.Cash.ToString(),
                    null,
                    Now.AddHours(1),
                    Guid.NewGuid(),
                    CanViewAll: true,
                    ClientRequestId: $"bil-bb2-{Guid.NewGuid():N}"
                )
            );

    private async Task<InvoiceStatus> StatusAsync()
    {
        await using var db = NewContext();
        return await db
            .Invoices.IgnoreQueryFilters()
            .Where(i => i.Id == _invoiceId)
            .Select(i => i.Status)
            .SingleAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "bil-bb2-durable-local-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(RecordManualPaymentHandler))
                    .IncludeType(typeof(InvoicePaidDomainEventHandler))
                    .IncludeType(typeof(GenerateInvoicePdfHandler));
                options.PersistMessagesWithSqlServer(_connectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<BillingDbContext>(db =>
                    db.UseSqlServer(_connectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BillingDbContext>());
                options.Services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                options.Services.AddSingleton<ICorrelationContext>(new FixedCorrelation());
                options.Services.AddSingleton(TimeProvider.System);
                options.Services.AddSingleton(Options.Create(new BillingVisibilityOptions()));
                options.Services.AddSingleton<IInvoiceDocumentClient>(_documents);
                options.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, BillingDbContext>();
                options.Policies.AutoApplyTransactions();

                // Igual que Program.cs (BIL-BB1 y BIL-BB2).
                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options.PublishMessage<InvoicePaidIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private BillingDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private Task<int> CountRowsAsync(string table, string messageType, string? status = null) =>
        CountAsync(
            $"IF OBJECT_ID('[{_schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[{table}] WHERE message_type LIKE '%{messageType}%'"
                + (status is null ? string.Empty : $" AND status = '{status}'")
        );

    private async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // Borra las tablas de Wolverine del esquema de prueba (primero las FK) y el esquema.
    private Task DropSchemaAsync() =>
        ExecuteAsync(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{_schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{_schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{_schema}') + N';';
            EXEC sp_executesql @sql;
            """
        );

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "bil-bb2-test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    // Documents que no responde hasta Release(): deja el comando de PDF a medio procesar.
    private sealed class RecordingDocumentClient : IInvoiceDocumentClient
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<Guid> Calls { get; } = new();

        public void Release() => _gate.TrySetResult();

        public async Task<Result> GenerateAsync(
            InvoiceDocumentRequest request,
            Guid tenantId,
            string idempotencyKey,
            string? correlationId,
            CancellationToken ct = default
        )
        {
            Calls.Enqueue(request.InvoiceId);
            await _gate.Task.WaitAsync(ct);
            return Result.Success();
        }
    }

    // Falla después de guardar el pago y antes de confirmar: la transacción de Wolverine deshace fila y sobres.
    private sealed class FailAfterPaidInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var hit =
                Fail
                && eventData.Context!.ChangeTracker.Entries<Invoice>().Any(e => e.Entity.Status == InvoiceStatus.Paid);
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
