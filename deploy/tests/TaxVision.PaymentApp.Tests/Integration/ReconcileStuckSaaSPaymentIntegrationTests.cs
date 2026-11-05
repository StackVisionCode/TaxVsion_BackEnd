using System.Collections.Concurrent;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>
/// PAY-2 contra el SQL Server local: cada pago atascado se resuelve en su propia transacción con outbox.
/// Un webhook que gana la carrera sobre A tira solo el guardado de A (rowversion) y no deja eventos suyos;
/// B se confirma y publica igual. Los sobres van a un esquema propio y a una cola local que hace de sumidero.
/// </summary>
public sealed class ReconcileStuckSaaSPaymentIntegrationTests : IAsyncLifetime
{
    private const string Schema = "pay2_reconcile_test";

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("PAYMENTAPP_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-paymentapp001")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set PAYMENTAPP_DB_CONNECTION or the PaymentApp user-secrets to run this test."
        );

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _providerReference = $"pi_pay2_{Guid.NewGuid():N}";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Solo las filas de este tenant y del esquema de prueba.
        await ExecuteAsync(
            "DELETE a FROM dbo.SaaSPaymentAttempts a JOIN dbo.SaaSPayments p ON p.Id = a.SaaSPaymentId WHERE p.TenantId = @tenant; "
                + "DELETE FROM dbo.SaaSPayments WHERE TenantId = @tenant; "
                + $"IF OBJECT_ID('{Schema}.wolverine_incoming_envelopes') IS NOT NULL DELETE FROM {Schema}.wolverine_incoming_envelopes; "
                + $"IF OBJECT_ID('{Schema}.wolverine_outgoing_envelopes') IS NOT NULL DELETE FROM {Schema}.wolverine_outgoing_envelopes; "
                + $"IF OBJECT_ID('{Schema}.wolverine_dead_letters') IS NOT NULL DELETE FROM {Schema}.wolverine_dead_letters;"
        );
    }

    [Fact]
    public async Task Concurrent_webhook_update_rolls_back_only_that_payment_and_leaves_no_envelope()
    {
        var paymentA = ProcessingRenewal("A");
        var paymentB = ProcessingRenewal("B");
        await using (var seed = CreateContext())
        {
            seed.SaaSPayments.AddRange(paymentA, paymentB);
            await seed.SaveChangesAsync();
        }

        // Cuando el handler de A pregunta al provider, otro contexto (el webhook) confirma A primero.
        var provider = new StatusPaymentProvider(
            Result.Success(new ChargeAuthorizationResult(_providerReference, PaymentStatus.Succeeded)),
            async reference =>
            {
                if (reference != paymentA.ExternalChargeReference!.Value)
                    return;
                await using var webhook = CreateContext();
                var winner = await webhook.SaaSPayments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentA.Id);
                Assert.True(winner.MarkSucceeded(DateTime.UtcNow, Guid.Empty).IsSuccess);
                await webhook.SaveChangesAsync();
            }
        );

        using var host = await StartHostAsync(provider);
        var bus = host.Services.GetRequiredService<IMessageBus>();

        var errorA = await Record.ExceptionAsync(() =>
            bus.InvokeAsync<Result>(new ReconcileStuckSaaSPaymentCommand(_tenantId, paymentA.Id))
        );
        var resultB = await bus.InvokeAsync<Result>(new ReconcileStuckSaaSPaymentCommand(_tenantId, paymentB.Id));

        Assert.IsType<DbUpdateConcurrencyException>(errorA);
        Assert.True(resultB.IsSuccess);

        await WaitUntilAsync(() =>
            Sink.ForTenant(_tenantId).Any(m => m is SubscriptionRenewalPaymentSucceededIntegrationEvent)
        );
        // Margen para que un sobre de A, si existiera, también llegara.
        await Task.Delay(TimeSpan.FromSeconds(2));

        var delivered = Sink.ForTenant(_tenantId);
        var renewalEvent = Assert.Single(delivered.OfType<SubscriptionRenewalPaymentSucceededIntegrationEvent>());
        Assert.Equal(paymentB.Id, renewalEvent.SaaSPaymentId);
        Assert.DoesNotContain(delivered, m => PaymentIdOf(m) == paymentA.Id);

        await using var read = CreateContext();
        var storedA = await read.SaaSPayments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == paymentA.Id);
        var storedB = await read.SaaSPayments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == paymentB.Id);
        Assert.Equal(PaymentStatus.Succeeded, storedA.Status);
        // A conserva lo que escribió el webhook: la referencia del handler perdedor no se guardó.
        Assert.Equal(paymentA.ExternalChargeReference!.Value, storedA.ExternalChargeReference!.Value);
        Assert.Equal(PaymentStatus.Succeeded, storedB.Status);
        Assert.Equal(_providerReference, storedB.ExternalChargeReference!.Value);

        Assert.Equal(0, await CountAsync($"{Schema}.wolverine_outgoing_envelopes"));
        Assert.Equal(0, await CountAsync($"{Schema}.wolverine_dead_letters"));

        await host.StopAsync();
    }

    private async Task<IHost> StartHostAsync(IPaymentProvider provider) =>
        await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<PaymentAppDbContext>(options => options.UseSqlServer(_connectionString));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentAppDbContext>());
                services.AddScoped<ITenantContext, TestTenantContext>();
                services.AddScoped<ISaaSPaymentRepository, SaaSPaymentRepository>();
                services.AddSingleton<IPaymentAdapterFactory>(new FakePaymentAdapterFactory(provider));
                services.AddSingleton<ITenantRegistry>(new FakeTenantRegistry("Acme Tax"));
                services.AddSingleton<ITenantProviderCustomerRepository>(new NoSavedCustomers());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            })
            .UseWolverine(options =>
            {
                options.ServiceName = "pay2-reconcile-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ReconcileStuckSaaSPaymentHandler))
                    .IncludeType(typeof(SinkHandler));
                options.PersistMessagesWithSqlServer(_connectionString, Schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, PaymentAppDbContext>();
                options.Policies.AutoApplyTransactions();
                // Sin RabbitMQ: los eventos van a una cola local durable que los registra.
                options
                    .PublishMessage<SubscriptionRenewalPaymentSucceededIntegrationEvent>()
                    .ToLocalQueue("pay2-sink")
                    .UseDurableInbox();
                options
                    .PublishMessage<SaaSPaymentSucceededIntegrationEvent>()
                    .ToLocalQueue("pay2-sink")
                    .UseDurableInbox();
            })
            .StartAsync();

    private SaaSPayment ProcessingRenewal(string label)
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pay2-{label}-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, $"pay2-{label}-{Guid.NewGuid():N}").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        return payment;
    }

    private PaymentAppDbContext CreateContext()
    {
        var tenant = new TestTenantContext();
        tenant.SetTenant(_tenantId);
        return new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(_connectionString).Options,
            tenant
        );
    }

    private static Guid? PaymentIdOf(object message) =>
        message switch
        {
            SubscriptionRenewalPaymentSucceededIntegrationEvent e => e.SaaSPaymentId,
            SaaSPaymentSucceededIntegrationEvent e => e.SaaSPaymentId,
            _ => null,
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new Xunit.Sdk.XunitException("The expected event never reached the sink.");
            await Task.Delay(200);
        }
    }

    private async Task<int> CountAsync(string table)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF OBJECT_ID('{table}') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@tenant", _tenantId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Registra lo que sale por el outbox, por tenant.</summary>
    public static class Sink
    {
        private static readonly ConcurrentQueue<(Guid TenantId, object Message)> Received = new();

        public static void Add(Guid tenantId, object message) => Received.Enqueue((tenantId, message));

        public static IReadOnlyList<object> ForTenant(Guid tenantId) =>
            Received.Where(r => r.TenantId == tenantId).Select(r => r.Message).ToList();
    }

    public static class SinkHandler
    {
        public static void Handle(SubscriptionRenewalPaymentSucceededIntegrationEvent message) =>
            Sink.Add(message.TenantId, message);

        public static void Handle(SaaSPaymentSucceededIntegrationEvent message) => Sink.Add(message.TenantId, message);
    }

    // Público: el código que genera Wolverine lo construye directamente.
    public sealed class TestTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? Guid.Empty;
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private sealed class NoSavedCustomers : ITenantProviderCustomerRepository
    {
        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>([]);

        public Task AddAsync(TenantProviderCustomer added, CancellationToken ct = default) => Task.CompletedTask;
    }
}
