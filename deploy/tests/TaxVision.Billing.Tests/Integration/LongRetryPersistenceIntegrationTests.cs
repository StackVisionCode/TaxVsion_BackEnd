using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Messaging;
using TaxVision.Billing.Tests.Fakes;
using Wolverine;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-3b contra el SQL Server real: el reintento largo queda guardado como Scheduled en
/// wolverine_incoming_envelopes y se ejecuta después de reiniciar el host. Usa un esquema propio
/// (<see cref="Schema"/>), no el "integration" de Billing, dentro de la base temporal (BIL-14).
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class LongRetryPersistenceIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private const string Schema = "bil3b_retry_test";
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _connectionString = database.ConnectionString;

    private readonly Invoice _invoice = IssuedInvoice();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Solo las filas de este esquema de prueba.
        await ExecuteAsync(
            $"IF OBJECT_ID('{Schema}.wolverine_incoming_envelopes') IS NOT NULL DELETE FROM {Schema}.wolverine_incoming_envelopes; "
                + $"IF OBJECT_ID('{Schema}.wolverine_dead_letters') IS NOT NULL DELETE FROM {Schema}.wolverine_dead_letters;"
        );
    }

    [Fact]
    public async Task ScheduledRetry_IsPersisted_AndRunsAfterRestart()
    {
        var firstRun = new RecordingInventoryClient { CommitResult = Unreachable() };
        using (var host = await StartHostAsync(firstRun))
        {
            await host
                .Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new SyncInvoiceStockCommand(_invoice.TenantId, _invoice.Id));

            var scheduled = await WaitForScheduledAsync(attempts: 1);
            Assert.Single(firstRun.Commits);
            AssertDelay(scheduled, LongRetryHandlerPolicy.Cooldowns[0]);

            await host.StopAsync();
        }

        // Billing apagado: el reintento sigue guardado.
        var afterStop = await ReadAsync();
        Assert.Equal(("Scheduled", 1), (afterStop!.Value.Status, afterStop.Value.Attempts));

        var secondRun = new RecordingInventoryClient { CommitResult = Unreachable() };
        using (var host = await StartHostAsync(secondRun))
        {
            var rescheduled = await WaitForScheduledAsync(attempts: 2);
            Assert.Single(secondRun.Commits);
            AssertDelay(rescheduled, LongRetryHandlerPolicy.Cooldowns[1]);

            await host.StopAsync();
        }
    }

    private async Task<IHost> StartHostAsync(RecordingInventoryClient inventory) =>
        await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IInventoryStockClient>(inventory);
                services.AddSingleton<IInvoiceRepository>(new InMemoryInvoiceRepository(_invoice));
                services.AddSingleton<IUnitOfWork>(new CountingUnitOfWork());
                services.AddSingleton<IBillingMetrics>(new RecordingBillingMetrics());
            })
            .UseWolverine(options =>
            {
                options.ServiceName = "bil3b-retry-test";
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(SyncInvoiceStockHandler));
                options.PersistMessagesWithSqlServer(_connectionString, Schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                options.PublishMessage<SyncInvoiceStockCommand>().ToLocalQueue("billing-stock-sync").UseDurableInbox();
                options.Policies.Add(LongRetryHandlerPolicy.ForSyncCommands());
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

    private async Task<(string Status, int Attempts, DateTimeOffset ExecutionTime, DateTimeOffset SeenAt)> WaitForScheduledAsync(
        int attempts
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var row = await ReadAsync();
            if (row is { Status: "Scheduled" } r && r.Attempts == attempts)
                return r;
            await Task.Delay(250);
        }

        var last = await ReadAsync();
        throw new Xunit.Sdk.XunitException(
            $"No Scheduled envelope with attempts={attempts}. Last row: {(last is null ? "none" : $"{last.Value.Status}/{last.Value.Attempts}")}; "
                + $"dead letters: {await CountDeadLettersAsync()}."
        );
    }

    private async Task<(string Status, int Attempts, DateTimeOffset ExecutionTime, DateTimeOffset SeenAt)?> ReadAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('{Schema}.wolverine_incoming_envelopes') IS NOT NULL "
            + $"SELECT TOP 1 status, attempts, execution_time, SYSDATETIMEOFFSET() FROM {Schema}.wolverine_incoming_envelopes "
            + "WHERE message_type LIKE '%SyncInvoiceStockCommand%'";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        var execution = reader.IsDBNull(2) ? DateTimeOffset.MinValue : ToOffset(reader.GetValue(2));
        return (reader.GetString(0), reader.GetInt32(1), execution, reader.GetDateTimeOffset(3));
    }

    private async Task<int> CountDeadLettersAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('{Schema}.wolverine_dead_letters') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM {Schema}.wolverine_dead_letters";
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

    private static DateTimeOffset ToOffset(object value) =>
        value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Unexpected execution_time type {value.GetType()}."),
        };

    // El reintento se programa para ~Delay después del fallo; se ve unos instantes después.
    private static void AssertDelay(
        (string Status, int Attempts, DateTimeOffset ExecutionTime, DateTimeOffset SeenAt) row,
        TimeSpan delay
    )
    {
        var ahead = row.ExecutionTime - row.SeenAt;
        Assert.InRange(ahead, delay - TimeSpan.FromSeconds(3), delay + TimeSpan.FromSeconds(1));
    }

    private static Result Unreachable() => Result.Failure(new Error("Billing.Inventory.Unreachable", "down"));

    private static Invoice IssuedInvoice()
    {
        var invoice = Invoice
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 2_000, 0, Guid.NewGuid())],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess);
        return invoice;
    }
}
