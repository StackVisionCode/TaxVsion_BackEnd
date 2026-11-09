using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.PrepareTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.RecordTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.DomainEventHandlers;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;
using TaxVision.PaymentClient.Infrastructure.Scheduling;
using TaxVision.PaymentClient.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// PCL-BB1 y PCL-BB3 contra SQL Server local: UseTaxVisionDomainEvents() con el PaymentClientDbContext real y la
/// revisión de webhooks pendientes en TransactionalJobUnit. Host mínimo de Wolverine, sobres en un esquema propio
/// que se borra al terminar, filas en un tenant propio que se borran por tenant, proveedor falso y avisos a un
/// puerto TCP cerrado (se quedan en wolverine_outgoing_envelopes y se pueden contar).
/// </summary>
public sealed class TenantPaymentDomainEventIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        PaymentClientIntegrationDatabase.ConnectionString;

    private static readonly PaymentWebhookOptions WebhookSettings = new()
    {
        PublicBaseUrl = "https://api.example.test",
        MaxAttempts = 3,
        RetryDelaySeconds = 60,
        PendingMarginMinutes = 15,
    };

    private const string ConnectWebhookSecret = "whsec_connect";

    private readonly string _schema = $"pclbb1_{Guid.NewGuid():N}"[..15];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FailAfterSaveInterceptor _interceptor = new();
    private readonly RecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"EXEC('CREATE SCHEMA [{_schema}]')");
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await using (var db = NewContext())
        {
            await db.AuditEntries.Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.WebhookEvents.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
            await db
                .TenantConnectAccounts.IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenantId)
                .ExecuteDeleteAsync();
            await db.TenantPayments.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.TenantPaymentConfigs.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
        }
        await DropSchemaAsync();
    }

    [Fact]
    public async Task DirectCharge_DomainEventStoredWithRow_AndSignalInOutbox()
    {
        await SeedConfigAsync();

        var result = await ChargeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, await StatusOfAsync(result.Value));
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "TenantPaymentSucceededDomainEvent"));

        // El handler del evento de dominio devuelve una sola señal "pagado".
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(TenantPaymentSucceededIntegrationEvent))
                == 1
            )
        );
    }

    [Fact]
    public async Task DirectCharge_FailureAfterSave_KeepsPendingRowAndNoEnvelope()
    {
        await SeedConfigAsync();
        _interceptor.FailOnSucceededPayment = true;

        var result = await ChargeAsync();

        // El resultado se revierte; la intención queda para que la reconciliación la encuentre.
        Assert.Equal(PaymentStatus.Pending, await StatusOfAsync(result.Value));
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "TenantPaymentSucceededDomainEvent"));
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(TenantPaymentSucceededIntegrationEvent))
        );
    }

    [Fact]
    public async Task PendingWebhookSweep_MarkAndCommandCommitTogether()
    {
        var configId = await SeedPendingWebhookSetupAsync(DateTime.UtcNow.AddHours(-1));
        var nowUtc = DateTime.UtcNow;

        var summary = await SweepAsync(nowUtc);

        Assert.Equal(new PendingWebhookSetupSweepSummary(1, 0), summary);
        Assert.Equal(nowUtc, await WebhookRequestedAtAsync(configId), TimeSpan.FromMilliseconds(10));
        Assert.Equal(
            1,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(RegisterTenantPaymentWebhookCommand))
        );
    }

    [Fact]
    public async Task PendingWebhookSweep_FailureAfterSave_NoMarkAndNoCommand()
    {
        var requestedAtUtc = DateTime.UtcNow.AddHours(-1);
        var configId = await SeedPendingWebhookSetupAsync(requestedAtUtc);
        _interceptor.FailOnWebhookConfig = true;

        var summary = await SweepAsync(DateTime.UtcNow);

        Assert.Equal(new PendingWebhookSetupSweepSummary(0, 0), summary);
        Assert.Equal(requestedAtUtc, await WebhookRequestedAtAsync(configId), TimeSpan.FromMilliseconds(10));
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(RegisterTenantPaymentWebhookCommand))
        );
    }

    [Fact]
    public async Task ConnectWebhook_ChargeSucceeded_SignalInOutboxOnce()
    {
        var (paymentId, payload) = await SeedConnectChargeAsync();

        Assert.True((await DeliverConnectAsync(payload)).IsSuccess);
        Assert.True((await DeliverConnectAsync(payload)).IsSuccess);

        Assert.Equal(PaymentStatus.Succeeded, await StatusOfAsync(paymentId));
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "TenantPaymentSucceededDomainEvent"));
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(TenantPaymentSucceededIntegrationEvent))
                == 1
            )
        );
    }

    [Fact]
    public async Task ConnectWebhook_FailureAfterSave_NoSignalAndRedeliveryApplies()
    {
        var (paymentId, payload) = await SeedConnectChargeAsync();
        _interceptor.FailOnSucceededPayment = true;

        Assert.NotNull(await Record.ExceptionAsync(() => DeliverConnectAsync(payload)));

        Assert.Equal(PaymentStatus.RequiresAction, await StatusOfAsync(paymentId));
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "TenantPaymentSucceededDomainEvent"));

        _interceptor.FailOnSucceededPayment = false;
        Assert.True((await DeliverConnectAsync(payload)).IsSuccess);

        Assert.Equal(PaymentStatus.Succeeded, await StatusOfAsync(paymentId));
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "TenantPaymentSucceededDomainEvent"));
    }

    private Task<Result> DeliverConnectAsync(string payload) =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new ProcessConnectWebhookCommand(payload, SignConnect(payload)));

    private async Task<(Guid PaymentId, string Payload)> SeedConnectChargeAsync()
    {
        var accountId = $"acct_{Guid.NewGuid():N}";
        var reference = $"pi_{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var payment = TenantPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pcl-9-{Guid.NewGuid():N}").Value,
                Money.Create(2500, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-pcl9").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                now
            )
            .Value;
        var split = SplitPaymentBreakdown.Create(2400, 100, null).Value;
        Assert.True(payment.MarkProcessingViaConnect(reference, split, null, null, Guid.Empty, now).IsSuccess);
        Assert.True(payment.MarkRequiresAction("use_stripe_sdk", "", Guid.Empty, now).IsSuccess);

        await using var db = NewContext();
        db.TenantConnectAccounts.Add(
            TenantConnectAccount
                .Create(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    ConnectAccountType.Express,
                    StripeConnectAccountId.Create(accountId).Value,
                    now
                )
                .Value
        );
        db.TenantPayments.Add(payment);
        await db.SaveChangesAsync();

        var payload =
            "{\"id\":\"evt_"
            + Guid.NewGuid().ToString("N")
            + "\",\"object\":\"event\",\"type\":\"payment_intent.succeeded\","
            + "\"api_version\":\""
            + StripeConfiguration.ApiVersion
            + "\",\"created\":1762171200,\"livemode\":false,"
            + "\"pending_webhooks\":1,\"account\":\""
            + accountId
            + "\",\"request\":{\"id\":null,\"idempotency_key\":null},"
            + "\"data\":{\"object\":{\"id\":\""
            + reference
            + "\",\"object\":\"payment_intent\",\"amount\":2500,"
            + "\"currency\":\"usd\",\"status\":\"succeeded\"}}}";
        return (payment.Id, payload);
    }

    // Formato de Stripe: t=<unix>,v1=HMAC-SHA256(secret, "<t>.<payload>").
    private static string SignConnect(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(ConnectWebhookSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")
        );
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    private Task<Result<Guid>> ChargeAsync() =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<Guid>>(
                new ChargeTenantPaymentCommand(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    2500,
                    "USD",
                    TaxpayerId: null,
                    PaymentPurposeKind.InvoicePayment,
                    PurposeExternalReferenceId: "inv-bb1",
                    PaymentMethodReference: "pm_card",
                    ReceiptEmail: null,
                    IdempotencyKey: $"pcl-bb1-{Guid.NewGuid():N}",
                    ActorUserId: Guid.NewGuid()
                )
            );

    private async Task<PendingWebhookSetupSweepSummary> SweepAsync(DateTime nowUtc)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await PendingWebhookSetupSweepJob.SweepAsync(
            new StaleOfTenant(scope.ServiceProvider.GetRequiredService<ITenantPaymentConfigRepository>(), _tenantId),
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            WebhookSettings,
            NullLogger.Instance,
            nowUtc,
            CancellationToken.None
        );
    }

    private async Task SeedConfigAsync()
    {
        var config = NewConfig();
        config.MarkActive(Guid.Empty, DateTime.UtcNow);
        await using var db = NewContext();
        db.TenantPaymentConfigs.Add(config);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPendingWebhookSetupAsync(DateTime requestedAtUtc)
    {
        var config = NewConfig();
        config.BeginAutomaticWebhookSetup(requestedAtUtc);
        await using var db = NewContext();
        db.TenantPaymentConfigs.Add(config);
        await db.SaveChangesAsync();
        return config.Id;
    }

    private TenantPaymentConfig NewConfig()
    {
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                DateTime.UtcNow
            )
            .Value;
        config.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            DateTime.UtcNow
        );
        return config;
    }

    private async Task<PaymentStatus> StatusOfAsync(Guid paymentId)
    {
        await using var db = NewContext();
        return await db
            .TenantPayments.IgnoreQueryFilters()
            .Where(p => p.Id == paymentId)
            .Select(p => p.Status)
            .SingleAsync();
    }

    private async Task<int> CountPaymentsAsync()
    {
        await using var db = NewContext();
        return await db.TenantPayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == _tenantId);
    }

    private async Task<DateTime> WebhookRequestedAtAsync(Guid configId)
    {
        await using var db = NewContext();
        var requestedAt = await db
            .TenantPaymentConfigs.IgnoreQueryFilters()
            .Where(c => c.Id == configId)
            .Select(c => c.WebhookSetupRequestedAtUtc)
            .SingleAsync();
        return requestedAt ?? DateTime.MinValue;
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "pcl-bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ChargeTenantPaymentHandler))
                    .IncludeType(typeof(PrepareTenantPaymentHandler))
                    .IncludeType(typeof(ExecuteTenantPaymentChargeHandler))
                    .IncludeType(typeof(RecordTenantPaymentChargeHandler))
                    .IncludeType(typeof(ProcessConnectWebhookHandler))
                    .IncludeType(typeof(TenantPaymentSucceededDomainEventHandler));
                options.PersistMessagesWithSqlServer(ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<PaymentClientDbContext>(db =>
                    db.UseSqlServer(ConnectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentClientDbContext>());
                options.Services.AddSingleton<ITenantContext, NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext, FixedCorrelationContext>();
                options.Services.AddScoped<ITenantPaymentRepository, TenantPaymentRepository>();
                options.Services.AddScoped<ITenantPaymentConfigRepository, TenantPaymentConfigRepository>();
                options.Services.AddScoped<ITenantConnectAccountRepository, TenantConnectAccountRepository>();
                options.Services.AddScoped<IPaymentLinkRepository, PaymentLinkRepository>();
                options.Services.AddScoped<IPaymentAuditLogWriter, PaymentAuditLogWriter>();
                options.Services.AddSingleton(InMemoryPaymentClient.FakeAdapters(_stripe));
                options.Services.AddSingleton<ISecretProtector, PrefixSecretProtector>();
                options.Services.AddSingleton<IPaymentClientMetrics, NoOpPaymentClientMetrics>();
                options.Services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
                options.Services.AddScoped<IPayoutScheduleRepository, PayoutScheduleRepository>();
                options.Services.AddSingleton<IStripeConnectGateway, StripeConnectGateway>();
                options.Services.AddSingleton(
                    Options.Create(
                        new PlatformStripeCredentials
                        {
                            PlatformSecretKey = "sk_unused",
                            ConnectWebhookSecret = ConnectWebhookSecret,
                        }
                    )
                );
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, PaymentClientDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();

                options.PublishMessage<TenantPaymentSucceededIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<RegisterTenantPaymentWebhookCommand>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private PaymentClientDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    private Task<int> CountRowsAsync(string table, string messageType) =>
        CountAsync(
            $"IF OBJECT_ID('[{_schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[{table}] WHERE message_type LIKE '%{messageType}%'"
        );

    private static async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
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

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    /// <summary>La revisión es cross-tenant: en la prueba solo ve los pendientes de su propio tenant.</summary>
    private sealed class StaleOfTenant(ITenantPaymentConfigRepository inner, Guid tenantId)
        : ITenantPaymentConfigRepository
    {
        public async Task<IReadOnlyList<TenantPaymentConfig>> GetStalePendingWebhookSetupsAsync(
            DateTime requestedBeforeUtc,
            int batchSize,
            CancellationToken ct = default
        ) =>
            [
                .. (await inner.GetStalePendingWebhookSetupsAsync(requestedBeforeUtc, 500, ct)).Where(c =>
                    c.TenantId == tenantId
                ),
            ];

        public Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<TenantPaymentConfig?> GetByIdAsync(
            Guid tenantPaymentConfigId,
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantPaymentConfig>> GetAllByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Remove(TenantPaymentConfig config) => throw new NotSupportedException();
    }

    // Falla después de guardar y antes de confirmar: la transacción (del handler o de la unidad) deshace fila y sobre.
    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool FailOnSucceededPayment { get; set; }
        public bool FailOnWebhookConfig { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var tracker = eventData.Context!.ChangeTracker;
            var hit =
                (
                    FailOnSucceededPayment
                    && tracker.Entries<TenantPayment>().Any(e => e.Entity.Status == PaymentStatus.Succeeded)
                ) || (FailOnWebhookConfig && tracker.Entries<TenantPaymentConfig>().Any());
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
