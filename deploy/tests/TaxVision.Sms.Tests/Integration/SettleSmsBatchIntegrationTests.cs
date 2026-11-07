using System.Collections.Concurrent;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks.Commands;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Infrastructure.Messaging;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;
using TaxVision.Sms.Tests.Fakes;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Sms.Tests.Integration;

/// <summary>
/// La liquidación que falla al enviar contra SQL Server local, en una base temporal propia y con la cola durable
/// real: el SMS sale una vez, el cobro no se pierde y no se cobra dos veces.
/// </summary>
public sealed class SettleSmsBatchIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "smssettle_";
    private const string EnvelopeSchema = "smssettle";

    private static readonly TimeSpan[] ShortDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
    ];

    private readonly string _connectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("SMS_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("b8e2f1a4-7c3d-4e9a-9f21-3c5d8a1b6e42")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set SMS_DB_CONNECTION or the Sms user-secrets to run this test.")
    )
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeSmsProvider _provider = new();
    private readonly IdempotentWallet _wallet = new();

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task WalletDownAtSend_SettlesFromTheDurableQueue()
    {
        _wallet.FailNextSettles(3);
        using var host = await StartHostAsync(ShortDelays);

        var result = await SendAsync(host);

        Assert.True(result.IsSuccess);
        Assert.Equal("Accepted", Assert.Single(result.Value.Results).Status);
        Assert.True(await WaitUntilAsync(() => Task.FromResult(_wallet.Charged.Count == 1)));
        var charge = Assert.Single(_wallet.Charged);
        Assert.Equal(result.Value.BatchId, charge.ReferenceId);
        Assert.Equal(1, charge.ConsumedUnits);
        // El reintento va con M2M: el bearer de la sesión no se guarda en la cola.
        Assert.Null(charge.CallerBearerToken);
        Assert.Equal(1, _provider.SendAsyncCallCount);

        await host.StopAsync();
    }

    [Fact]
    public async Task WalletAppliedButResponseLost_RetryDoesNotChargeTwice()
    {
        _wallet.LoseNextResponses(1);
        using var host = await StartHostAsync(ShortDelays);

        await SendAsync(host);

        Assert.True(await WaitUntilAsync(() => Task.FromResult(_wallet.Calls.Count >= 2)));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Single(_wallet.Charged);

        await host.StopAsync();
    }

    [Fact]
    public async Task PendingSettle_SurvivesARestart()
    {
        _wallet.FailNextSettles(2);
        var delays = new[] { TimeSpan.FromSeconds(3) };
        using (var first = await StartHostAsync(delays))
        {
            await SendAsync(first);
            Assert.True(await WaitUntilAsync(async () => await CountScheduledSettlesAsync() == 1));
            await first.StopAsync();
        }
        Assert.Empty(_wallet.Charged);

        using var second = await StartHostAsync(delays);

        Assert.True(await WaitUntilAsync(() => Task.FromResult(_wallet.Charged.Count == 1)));

        await second.StopAsync();
    }

    [Fact]
    public async Task RetriesExhausted_LeavesTheSettlementInTheErrorQueue()
    {
        _wallet.FailNextSettles(int.MaxValue);
        using var host = await StartHostAsync([TimeSpan.FromSeconds(1)]);

        await SendAsync(host);

        Assert.True(await WaitUntilAsync(async () => await CountDeadSettlesAsync() == 1));
        Assert.Empty(_wallet.Charged);

        await host.StopAsync();
    }

    [Fact]
    public async Task ReconciledBatch_WalletDown_SettlesFromTheDurableQueue()
    {
        var batchId = Guid.NewGuid();
        var attemptedAt = DateTime.UtcNow.AddHours(-2);
        var message = SmsMessage
            .Create(
                _tenantId,
                Guid.NewGuid(),
                PhoneE164.Create("+15550201234").Value,
                SmsBody.Create("integration test").Value,
                "it-key-2",
                "it-corr",
                batchId,
                "fake",
                "integration",
                [],
                attemptedAt,
                walletCharged: true
            )
            .Value;
        message.MarkSendAttempted(attemptedAt);
        message.MarkOutcomeUnknown(attemptedAt, null, "sms.providerOutcomeUnknown", "timeout");
        await using (var seed = NewContext())
        {
            seed.SmsMessages.Add(message);
            await seed.SaveChangesAsync();
        }
        _wallet.FailNextSettles(2);
        using var host = await StartHostAsync(ShortDelays);

        var result = await SmsReconciliation.RunAsync(
            host.Services.GetRequiredService<IMessageBus>(),
            new ReconcileSmsStatusesCommand(_tenantId),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(await WaitUntilAsync(() => Task.FromResult(_wallet.Charged.Count == 1)));
        var charge = Assert.Single(_wallet.Charged);
        Assert.Equal(batchId, charge.ReferenceId);
        Assert.Equal(0, charge.ConsumedUnits);

        await host.StopAsync();
    }

    private async Task<Result<SendSmsBatchResponse>> SendAsync(IHost host)
    {
        var bus = host.Services.GetRequiredService<IMessageBus>();
        return await bus.InvokeAsync<Result<SendSmsBatchResponse>>(
            new SendSmsBatchCommand(
                _tenantId,
                "it-corr",
                [
                    new SmsSendItemDto(
                        Guid.NewGuid(),
                        "+15550201234",
                        "integration test",
                        null,
                        "it-key-1",
                        "integration"
                    ),
                ],
                ActorUserId: null,
                CanViewAll: true,
                ChargeWallet: true,
                CallerBearerToken: "bearer-token",
                BatchKey: "it-batch"
            )
        );
    }

    private async Task<IHost> StartHostAsync(TimeSpan[] delays) =>
        await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<SmsDbContext>(options => options.UseSqlServer(_connectionString));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SmsDbContext>());
                services.AddSingleton<ITenantContext>(new NoTenant());
                services.AddScoped<ISmsMessageRepository, SmsMessageRepository>();
                services.AddScoped<ISmsOptOutRepository, SmsOptOutRepository>();
                services.AddScoped<IPendingDeliveryReceiptRepository, PendingDeliveryReceiptRepository>();
                services.AddScoped<IProcessedWebhookRepository, ProcessedWebhookRepository>();
                services.AddSingleton<ISmsProviderRouter>(new FakeSmsProviderRouter([_provider]));
                services.AddSingleton<ISmsAdapterFactory>(new FakeSmsAdapterFactory(_provider));
                services.AddSingleton(Options.Create(new SmsOptions { DefaultProvider = "fake" }));
                services.AddSingleton(Options.Create(new SmsVisibilityOptions()));
                services.AddSingleton<ISmsCustomerAssignmentReader>(new FakeSmsCustomerAssignmentReader());
                services.AddSingleton<ICustomerContactPolicyRepository>(new FakeCustomerContactPolicyRepository());
                services.AddSingleton<IWalletSpendClient>(_wallet);
            })
            .UseWolverine(options =>
            {
                options.ServiceName = "sms-settle-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(SendSmsBatchHandler))
                    .IncludeType(typeof(SettleSmsBatchHandler))
                    .IncludeType(typeof(ReserveSmsMessageHandler))
                    .IncludeType(typeof(RecordSmsSendResultHandler))
                    .IncludeType(typeof(ReconcileSmsStatusesHandler))
                    .IncludeType(typeof(ApplySmsReconciliationHandler));
                options.PersistMessagesWithSqlServer(_connectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(500);
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, SmsDbContext>();
                options.Policies.AutoApplyTransactions();
                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
                options.Policies.Add(new WalletSettleRetryPolicy(new WalletSettleRetryOptions { Delays = delays }));
                options.UseTaxVisionDurableLocalQueues();
            })
            .StartAsync();

    private Task<int> CountScheduledSettlesAsync() =>
        CountAsync(
            $"SELECT COUNT(*) FROM [{EnvelopeSchema}].[wolverine_incoming_envelopes] "
                + "WHERE message_type LIKE '%SettleSmsBatchCommand%' AND status = 'Scheduled'"
        );

    private Task<int> CountDeadSettlesAsync() =>
        CountAsync(
            $"SELECT COUNT(*) FROM [{EnvelopeSchema}].[wolverine_dead_letters] "
                + "WHERE message_type LIKE '%SettleSmsBatchCommand%'"
        );

    private async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

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

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}%' AND create_date < DATEADD(HOUR, -1, GETDATE());
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private SmsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SmsDbContext>().UseSqlServer(_connectionString).Options, new NoTenant());

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Wallet con la regla real: liquidar la misma referencia otra vez no cobra de nuevo.</summary>
    private sealed class IdempotentWallet : IWalletSpendClient
    {
        private readonly ConcurrentDictionary<Guid, Settlement> _charged = new();
        private int _failures;
        private int _lostResponses;

        public ConcurrentQueue<Settlement> Calls { get; } = new();
        public IReadOnlyList<Settlement> Charged => [.. _charged.Values];

        public void FailNextSettles(int count) => _failures = count;

        public void LoseNextResponses(int count) => _lostResponses = count;

        public Task<WalletReserveResult> ReserveAsync(
            Guid tenantId,
            string referenceType,
            Guid referenceId,
            WalletUnitCounts units,
            string? callerBearerToken,
            CancellationToken ct = default
        ) => Task.FromResult(new WalletReserveResult(true, true, 0, 1_000_000, 0, "USD"));

        public Task SettleAsync(
            Guid tenantId,
            string referenceType,
            Guid referenceId,
            int consumedUnits,
            string? callerBearerToken,
            CancellationToken ct = default
        )
        {
            var call = new Settlement(referenceId, consumedUnits, callerBearerToken);
            Calls.Enqueue(call);
            if (Interlocked.Decrement(ref _failures) >= 0)
                throw new InvalidOperationException("Wallet settle failed (503).");

            _charged.TryAdd(referenceId, call);
            if (Interlocked.Decrement(ref _lostResponses) >= 0)
                throw new TimeoutException("Response lost after the Wallet applied the settlement.");
            return Task.CompletedTask;
        }
    }

    private sealed record Settlement(Guid ReferenceId, int ConsumedUnits, string? CallerBearerToken);
}
