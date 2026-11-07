using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.IntegrationEvents;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// BB-13. <see cref="AddOnCheckoutPaidConsumer"/> publica antes de guardar y entra por mensaje. Lo publicado se cuenta
/// al llegar a un puerto: contar <c>wolverine_outgoing_envelopes</c> da falso verde. Medido: por mensaje no se filtra
/// el aviso del intento revertido; por InvokeAsync, sí. Base temporal propia.
/// </summary>
public sealed class AddOnCheckoutPaidRetryIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "subbb13_";
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FailOnceInterceptor _failure = new();
    private readonly string _connectionString = new SqlConnectionStringBuilder(SubscriptionIntegrationDb.Connection)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    // Regla general: reintento en línea con espera, como un timeout de SQL.
    [Theory(Timeout = 120_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ByMessage_TransientSaveFailure_SendsOnlyTheStoredAddOn(bool localQueue)
    {
        var received = await DeliverAsync(localQueue, () => new TimeoutException("Simulated SQL timeout."));

        Assert.Equal([await StoredAddOnAsync()], received);
    }

    // UseTaxVisionConflictPolicy: reintento programado.
    [Theory(Timeout = 120_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ByMessage_ConcurrencyConflict_SendsOnlyTheStoredAddOn(bool localQueue)
    {
        var received = await DeliverAsync(
            localQueue,
            () => new ConflictException(WolverineConflictPolicy.ConcurrencyConflictCode, "Simulated conflict.")
        );

        Assert.Equal([await StoredAddOnAsync()], received);
    }

    // Control: con InvokeAsync el mismo fallo sí deja salir el aviso del intento revertido. Prueba que la medición
    // ve la filtración cuando la hay; si esto cambia, hay que volver a mirar la guía.
    [Fact(Timeout = 120_000)]
    public async Task ByInvoke_TransientSaveFailure_LeaksTheRevertedAddOn()
    {
        _host = await StartHostAsync(localQueue: false);
        var paid = await SeedPaidEventAsync();
        _failure.Next = () => new TimeoutException("Simulated SQL timeout.");

        await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(paid);

        var stored = await StoredAddOnAsync();
        await Task.Delay(SettleTime);
        var received = AddOnActivatedProbeHandler.ReceivedFor(_tenantId);
        Assert.Equal(1, _failure.Failures);
        Assert.Equal(2, received.Count);
        Assert.Contains(stored, received);
    }

    private async Task<IReadOnlyList<Guid>> DeliverAsync(bool localQueue, Func<Exception> failure)
    {
        _host = await StartHostAsync(localQueue);
        var paid = await SeedPaidEventAsync();
        _failure.Next = failure;

        await _host.Services.GetRequiredService<IMessageBus>().PublishAsync(paid);

        await StoredAddOnAsync();
        Assert.Equal(1, _failure.Failures);

        // Lo que tenga que llegar llega enseguida; la espera deja salir también lo que se hubiera colado.
        await Task.Delay(SettleTime);
        return AddOnActivatedProbeHandler.ReceivedFor(_tenantId);
    }

    private async Task<Guid> StoredAddOnAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            await using var db = NewContext();
            var ids = await db
                .TenantAddOns.IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenantId)
                .Select(a => a.Id)
                .ToListAsync();
            if (ids.Count > 0)
                return Assert.Single(ids);
            await Task.Delay(200);
        }
        throw new TimeoutException("The add-on was never provisioned.");
    }

    private async Task<AddOnCheckoutPaidIntegrationEvent> SeedPaidEventAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = AddOnTestCatalog.PlanWith($"plan-{Guid.NewGuid():N}"[..12], ["documents"]);
        var definition = AddOnTestCatalog.QuotaAddOn($"pack.{Guid.NewGuid():N}"[..12], allowMultiple: true);
        var subscription = TenantSubscription
            .ActivateImmediately(
                _tenantId,
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc.AddDays(-1),
                nowUtc.AddDays(29),
                Guid.Empty,
                nowUtc
            )
            .Value;
        var intent = AddOnPurchaseIntent
            .Create(
                _tenantId,
                definition,
                1,
                true,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                2900,
                Guid.NewGuid(),
                nowUtc
            )
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://checkout.test/addon", nowUtc.AddHours(1), nowUtc);

        await using var db = NewContext();
        db.Plans.Add(plan);
        db.AddOnDefinitions.Add(definition);
        db.Subscriptions.Add(subscription);
        db.AddOnPurchaseIntents.Add(intent);
        await db.SaveChangesAsync();
        return new AddOnCheckoutPaidIntegrationEvent
        {
            TenantId = _tenantId,
            AddOnPurchaseIntentId = intent.Id,
            SaaSPaymentId = Guid.NewGuid(),
            AmountPaidCents = 2900,
            Currency = "USD",
            PaidAtUtc = nowUtc,
            ProviderPaymentReference = "pi_test",
        };
    }

    private Task<IHost> StartHostAsync(bool localQueue) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var outbound = FreeTcpPort();
                options.ServiceName = "subscription-bb13-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(AddOnCheckoutPaidConsumer));
                options.Discovery.IncludeType(typeof(AddOnActivatedProbeHandler));
                options.PersistMessagesWithSqlServer(_connectionString, "wolverine_test");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(100);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(500);
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                var services = options.Services;
                services.AddBuildingBlocks();
                services.AddDbContext<SubscriptionDbContext>(db =>
                    db.UseSqlServer(_connectionString).AddInterceptors(_failure)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SubscriptionDbContext>());
                services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
                services.AddScoped<IAddOnPurchaseIntentRepository, AddOnPurchaseIntentRepository>();
                services.AddScoped<IAddOnDefinitionRepository, AddOnDefinitionRepository>();
                services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
                services.AddSingleton<ISubscriptionAuditLogWriter, FakeSubscriptionAuditLogWriter>();
                services.AddSingleton<ISubscriptionMetrics, FakeSubscriptionMetrics>();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
                options.Policies.AutoApplyTransactions();

                // Las dos entradas de un servicio: un listener con inbox durable, como Rabbit, y una cola local durable.
                if (localQueue)
                {
                    options
                        .PublishMessage<AddOnCheckoutPaidIntegrationEvent>()
                        .ToLocalQueue("bb13-paid")
                        .UseDurableInbox();
                }
                else
                {
                    var inbound = FreeTcpPort();
                    options.ListenAtPort(inbound).UseDurableInbox();
                    options.PublishMessage<AddOnCheckoutPaidIntegrationEvent>().ToPort(inbound).UseDurableOutbox();
                }

                options.ListenAtPort(outbound).UseDurableInbox();
                options.PublishMessage<AddOnActivatedIntegrationEvent>().ToPort(outbound).UseDurableOutbox();

                options.UseTaxVisionConflictPolicy(
                    new ConflictRetryOptions { Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)] }
                );
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

    private SubscriptionDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();

        var leftovers = new List<string>();
        await using (var find = connection.CreateCommand())
        {
            find.CommandText =
                $"SELECT name FROM sys.databases WHERE name LIKE '{Prefix.Replace("_", "[_]")}%' "
                + "AND create_date < DATEADD(HOUR, -1, GETDATE())";
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                leftovers.Add(reader.GetString(0));
        }

        foreach (var name in leftovers)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Falla una vez el guardado que crea el add-on.</summary>
    private sealed class FailOnceInterceptor : SaveChangesInterceptor
    {
        private int _failures;

        public Func<Exception>? Next { get; set; }
        public int Failures => Volatile.Read(ref _failures);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var creatingAddOn = eventData
                .Context!.ChangeTracker.Entries<TenantAddOn>()
                .Any(entry => entry.State == EntityState.Added);
            if (creatingAddOn && Next is { } failure)
            {
                Next = null;
                Interlocked.Increment(ref _failures);
                throw failure();
            }
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Lo que de verdad cruzó el puerto, por tenant.</summary>
public static class AddOnActivatedProbeHandler
{
    private static readonly ConcurrentQueue<(Guid TenantId, Guid AddOnId)> Received = new();

    public static IReadOnlyList<Guid> ReceivedFor(Guid tenantId) =>
        [.. Received.Where(item => item.TenantId == tenantId).Select(item => item.AddOnId)];

    public static void Handle(AddOnActivatedIntegrationEvent message) =>
        Received.Enqueue((message.TenantId, message.TenantAddOnId));
}
