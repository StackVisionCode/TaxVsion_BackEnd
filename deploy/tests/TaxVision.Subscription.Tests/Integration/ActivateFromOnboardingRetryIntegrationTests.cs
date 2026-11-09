using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
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
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.Commands;
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
/// SUB-17. <see cref="ActivateFromOnboardingHandler"/> entra por InvokeAsync, donde el reintento en línea conserva lo
/// publicado por el intento revertido. Lo enviado se cuenta al llegar a un puerto: contar
/// <c>wolverine_outgoing_envelopes</c> da falso verde. Base temporal propia.
/// </summary>
public sealed class ActivateFromOnboardingRetryIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "subs17_";
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
    public async Task TransientSaveFailure_SendsOnlyTheStoredSubscription()
    {
        _host = await StartHostAsync();
        var command = await SeedCommandAsync();
        _failure.Next = () => new TimeoutException("Simulated SQL timeout.");

        var result = await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync<Result>(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _failure.Failures);
        var stored = await StoredSubscriptionAsync();
        await Task.Delay(SettleTime);
        Assert.Equal([stored], OnboardingProbeHandler.ActivatedFor(_tenantId));
        Assert.Equal(1, OnboardingProbeHandler.RecalculationsFor(_tenantId));
    }

    private async Task<Guid> StoredSubscriptionAsync()
    {
        await using var db = NewContext();
        return await db
            .Subscriptions.IgnoreQueryFilters()
            .Where(s => s.TenantId == _tenantId)
            .Select(s => s.Id)
            .SingleAsync();
    }

    private async Task<ActivateFromOnboardingCommand> SeedCommandAsync()
    {
        var plan = AddOnTestCatalog.PlanWith($"plan-{Guid.NewGuid():N}"[..12], ["documents"]);
        await using var db = NewContext();
        db.Plans.Add(plan);
        await db.SaveChangesAsync();
        return new ActivateFromOnboardingCommand(
            Guid.NewGuid(),
            _tenantId,
            plan.Id,
            BillingCycle.Monthly,
            Guid.NewGuid()
        );
    }

    private Task<IHost> StartHostAsync() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var outbound = FreeTcpPort();
                options.ServiceName = "subscription-sub17-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(ActivateFromOnboardingHandler));
                options.Discovery.IncludeType(typeof(OnboardingProbeHandler));
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
                services.AddScoped<IPlanRepository, PlanRepository>();
                services.AddScoped<ISubscriptionTenantSettingsRepository, SubscriptionTenantSettingsRepository>();
                services.AddScoped<ITenantBillingPreferenceRepository, TenantBillingPreferenceRepository>();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
                options.Policies.AutoApplyTransactions();

                options.ListenAtPort(outbound).UseDurableInbox();
                options
                    .PublishMessage<SubscriptionActivatedForOnboardingIntegrationEvent>()
                    .ToPort(outbound)
                    .UseDurableOutbox();
                options.PublishMessage<RecalculateEntitlementsCommand>().ToPort(outbound).UseDurableOutbox();

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

    /// <summary>Falla una vez el guardado que crea la suscripción.</summary>
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
            var creating = eventData
                .Context!.ChangeTracker.Entries<TenantSubscription>()
                .Any(entry => entry.State == EntityState.Added);
            if (creating && Next is { } failure)
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
public static class OnboardingProbeHandler
{
    private static readonly ConcurrentQueue<(Guid TenantId, Guid SubscriptionId)> Activated = new();
    private static readonly ConcurrentQueue<Guid> Recalculations = new();

    public static IReadOnlyList<Guid> ActivatedFor(Guid tenantId) =>
        [.. Activated.Where(item => item.TenantId == tenantId).Select(item => item.SubscriptionId)];

    public static int RecalculationsFor(Guid tenantId) => Recalculations.Count(id => id == tenantId);

    public static void Handle(SubscriptionActivatedForOnboardingIntegrationEvent message) =>
        Activated.Enqueue((message.TenantId, message.CreatedSubscriptionId));

    public static void Handle(RecalculateEntitlementsCommand message) => Recalculations.Enqueue(message.TenantId);
}
