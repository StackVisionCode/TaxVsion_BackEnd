using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
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
using TaxVision.Subscription.Application.AddOns.Commands.CancelAddOn;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
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
/// BB-14. Comandos por InvokeAsync con un guardado que falla una vez como un timeout de SQL: el reintento en línea no
/// debe dejar salir lo del intento revertido. Lo publicado se cuenta al llegar a un puerto, en una base temporal propia.
/// </summary>
public sealed class CancelCommandsRetryIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "subbb14_";
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

    [Fact(Timeout = 120_000)]
    public async Task CancelAddOn_TransientSaveFailure_SendsOneNotice()
    {
        _host = await StartHostAsync();
        var addOnId = await SeedAddOnAsync();
        _failure.Next = entries => entries.Entries<TenantAddOn>().Any(entry => entry.State == EntityState.Modified);

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync(new CancelAddOnCommand(_tenantId, addOnId, "not needed", Guid.NewGuid()));

        await Task.Delay(SettleTime);
        Assert.Equal(1, _failure.Failures);
        Assert.Equal(1, CancelNoticeProbeHandler.CountFor<AddOnCancelledIntegrationEvent>(_tenantId));
    }

    [Fact(Timeout = 120_000)]
    public async Task CancelSubscription_TransientSaveFailure_SendsEachChargeMessageOnce()
    {
        _host = await StartHostAsync();
        await SeedSubscriptionWithPendingCheckoutAsync();
        _failure.Next = entries =>
            entries.Entries<TenantSubscription>().Any(entry => entry.State == EntityState.Modified);

        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync(new CancelSubscriptionCommand(_tenantId, "too expensive", Guid.NewGuid()));

        await Task.Delay(SettleTime);
        Assert.Equal(1, _failure.Failures);
        Assert.Equal(1, CancelNoticeProbeHandler.CountFor<PlanChangeUpgradeCancelledIntegrationEvent>(_tenantId));
        Assert.Equal(1, CancelNoticeProbeHandler.CountFor<CancelHostedCheckoutCommand>(_tenantId));
    }

    private async Task<Guid> SeedAddOnAsync()
    {
        var plan = AddOnTestCatalog.PlanWith($"plan-{Guid.NewGuid():N}"[..12], ["documents"]);
        var definition = AddOnTestCatalog.QuotaAddOn($"pack.{Guid.NewGuid():N}"[..12], allowMultiple: true);
        var addOn = AddOnTestCatalog.Owned(_tenantId, definition);
        await using var db = NewContext();
        db.Plans.Add(plan);
        db.AddOnDefinitions.Add(definition);
        db.Subscriptions.Add(AddOnTestCatalog.ActiveSubscription(_tenantId, plan));
        db.TenantAddOns.Add(addOn);
        await db.SaveChangesAsync();
        return addOn.Id;
    }

    private async Task SeedSubscriptionWithPendingCheckoutAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var code = $"plan-{Guid.NewGuid():N}"[..12];
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        var subscription = TenantSubscription
            .ActivateImmediately(
                _tenantId,
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
        Assert.True(
            subscription
                .RequestUpgrade(
                    plan,
                    version,
                    BillingCycle.Yearly,
                    10000,
                    "USD",
                    $"{Guid.NewGuid():N}",
                    Guid.Empty,
                    nowUtc
                )
                .IsSuccess
        );
        Assert.True(
            subscription
                .AttachUpgradeCheckout(
                    subscription.PlanChangeRequests.Single().Id,
                    Guid.NewGuid(),
                    "https://checkout.test/upgrade",
                    nowUtc.AddHours(1),
                    Guid.Empty,
                    nowUtc
                )
                .IsSuccess
        );

        await using var db = NewContext();
        db.Plans.Add(plan);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    private Task<IHost> StartHostAsync() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var outbound = FreeTcpPort();
                options.ServiceName = "subscription-bb14-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CancelAddOnHandler));
                options.Discovery.IncludeType(typeof(CancelSubscriptionHandler));
                options.Discovery.IncludeType(typeof(CancelNoticeProbeHandler));
                options.PersistMessagesWithSqlServer(_connectionString, "wolverine_test");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                var services = options.Services;
                services.AddBuildingBlocks();
                services.AddDbContext<SubscriptionDbContext>(db =>
                    db.UseSqlServer(_connectionString).AddInterceptors(_failure)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SubscriptionDbContext>());
                services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
                services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
                services.AddSingleton<ISubscriptionAuditLogWriter, FakeSubscriptionAuditLogWriter>();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
                options.Policies.AutoApplyTransactions();

                options.ListenAtPort(outbound).UseDurableInbox();
                options.PublishMessage<AddOnCancelledIntegrationEvent>().ToPort(outbound).UseDurableOutbox();
                options
                    .PublishMessage<PlanChangeUpgradeCancelledIntegrationEvent>()
                    .ToPort(outbound)
                    .UseDurableOutbox();
                options.PublishMessage<CancelHostedCheckoutCommand>().ToPort(outbound).UseDurableOutbox();

                options.UseTaxVisionConflictPolicy();
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

    /// <summary>Falla una vez el guardado que cumple la condición, como un timeout de SQL.</summary>
    private sealed class FailOnceInterceptor : SaveChangesInterceptor
    {
        private int _failures;

        public Func<Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker, bool>? Next { get; set; }
        public int Failures => Volatile.Read(ref _failures);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Next is { } condition && condition(eventData.Context!.ChangeTracker))
            {
                Next = null;
                Interlocked.Increment(ref _failures);
                throw new TimeoutException("Simulated SQL timeout.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Lo que de verdad cruzó el puerto, por tipo y tenant.</summary>
public static class CancelNoticeProbeHandler
{
    private static readonly ConcurrentQueue<(Type Message, Guid TenantId)> Received = new();

    public static int CountFor<T>(Guid tenantId) =>
        Received.Count(item => item.Message == typeof(T) && item.TenantId == tenantId);

    public static void Handle(AddOnCancelledIntegrationEvent message) =>
        Received.Enqueue((message.GetType(), message.TenantId));

    public static void Handle(PlanChangeUpgradeCancelledIntegrationEvent message) =>
        Received.Enqueue((message.GetType(), message.TenantId));

    public static void Handle(CancelHostedCheckoutCommand message) =>
        Received.Enqueue((message.GetType(), message.TenantId));
}
