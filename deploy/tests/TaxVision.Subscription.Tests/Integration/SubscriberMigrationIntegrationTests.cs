using System.Net;
using System.Net.Sockets;
using System.Text;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Web.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Catalog.Migrations;
using TaxVision.Subscription.Application.Subscriptions.DomainEvents;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// D11 de punta a punta contra SQL real (base temporal de las T-INT): vista previa → inicio con token → job (una
/// oficina por transacción) → aviso por outbox → la renovación aplica la edición agendada y cobra su precio. La
/// oficina que no entra en la edición nueva queda bloqueada y no se toca. Los avisos van a un puerto TCP cerrado y
/// se quedan en wolverine_outgoing_envelopes del esquema de la prueba.
/// </summary>
public sealed class SubscriberMigrationIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _fits = new();
    private readonly SubscriptionIntegrationDb _tooMany = new();
    private readonly string _schema = SubscriptionWolverineHost.NewSchema("submig");
    private IHost _host = null!;
    private SubscriptionPlan _plan = null!;
    private SubscriptionPlanVersion _from = null!;
    private SubscriptionPlanVersion _to = null!;

    public async Task InitializeAsync()
    {
        await SeedPlanAndOfficesAsync();
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await using (var db = SubscriptionIntegrationDb.NewContext(_fits.TenantId))
        {
            var ids = await db.SubscriberMigrations.Where(m => m.PlanId == _plan.Id).Select(m => m.Id).ToListAsync();
            await db.SubscriberMigrations.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync();
            await db
                .CatalogAuditEntries.Where(e => e.EntityId != null && ids.Contains(e.EntityId.Value))
                .ExecuteDeleteAsync();
        }
        await _fits.CleanupAsync();
        await _tooMany.CleanupAsync();
        await SubscriptionWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_migration_schedules_notifies_and_is_applied_by_the_renewal_while_a_blocked_office_stays()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        var preview = await bus.InvokeAsync<BuildingBlocks.Results.Result<SubscriberMigrationPreview>>(
            new PreviewSubscriberMigrationQuery(_plan.Id, _to.Id, "AtNextRenewal", 30)
        );
        Assert.True(preview.IsSuccess, preview.IsFailure ? preview.Error.Code : null);
        Assert.Null(preview.Value.Offices.Single(o => o.TenantId == _fits.TenantId).BlockedReason);
        Assert.Equal(
            "Migration.ExceedsSeats",
            preview.Value.Offices.Single(o => o.TenantId == _tooMany.TenantId).BlockedReason
        );

        var started = await bus.InvokeAsync<BuildingBlocks.Results.Result<Guid>>(
            new StartSubscriberMigrationCommand(
                _plan.Id,
                _to.Id,
                "AtNextRenewal",
                30,
                "New edition terms",
                preview.Value.PreviewToken,
                Guid.NewGuid()
            )
        );
        Assert.True(started.IsSuccess, started.IsFailure ? started.Error.Code : null);

        // Dos pasadas del job: la segunda no duplica nada.
        await RunJobAsync(started.Value);
        await RunJobAsync(started.Value);

        var migration = await LoadMigrationAsync(started.Value);
        Assert.Equal(SubscriberMigrationStatus.Completed, migration.Status);
        var scheduled = migration.Offices.Single(o => o.TenantId == _fits.TenantId);
        Assert.Equal(SubscriberMigrationOutcome.Scheduled, scheduled.Outcome);
        Assert.Equal("Migration.ExceedsSeats", migration.Offices.Single(o => o.TenantId == _tooMany.TenantId).Detail);

        var fits = await LoadSubscriptionAsync(_fits.TenantId);
        Assert.Equal(_from.Id, fits.PlanVersionId);
        Assert.Equal(_to.Id, fits.ScheduledVersionId);
        Assert.Equal(scheduled.EffectiveAtUtc, fits.ScheduledVersionAtUtc);
        Assert.True(fits.ScheduledVersionAtUtc >= DateTime.UtcNow.AddDays(30));

        var blocked = await LoadSubscriptionAsync(_tooMany.TenantId);
        Assert.Equal(_from.Id, blocked.PlanVersionId);
        Assert.Null(blocked.ScheduledVersionId);

        // Un solo aviso, el de la oficina agendada, con la fecha y el resumen.
        var notices = await WaitForBodiesAsync(nameof(TenantSubscriptionStatusChangedIntegrationEvent), 1);
        var notice = Assert.Single(notices);
        Assert.Contains("VersionChangeScheduled", notice);
        Assert.Contains(_fits.TenantId.ToString(), notice);
        Assert.Contains("$50.00", notice);
        Assert.Contains("$60.00", notice);

        // Llega la renovación: entra la edición nueva y se cobra a su precio.
        var renewal = _host.Services.GetRequiredService<TenantSubscriptionRenewalJob>();
        await renewal.RenewAsync([_fits.TenantId], fits.ScheduledVersionAtUtc!.Value.AddMinutes(1), "test", default);

        var renewed = await LoadSubscriptionAsync(_fits.TenantId);
        Assert.Equal(_to.Id, renewed.PlanVersionId);
        Assert.Null(renewed.ScheduledVersionId);
        var due = Assert.Single(await WaitForBodiesAsync(nameof(SubscriptionRenewalDueIntegrationEvent), 1));
        Assert.Contains("6000", due);
    }

    private async Task RunJobAsync(Guid migrationId)
    {
        var job = _host.Services.GetRequiredService<SubscriberMigrationJob>();
        var snapshot = await LoadMigrationAsync(migrationId);
        foreach (var tenantId in snapshot.PendingTenantIds())
            await job.ProcessOfficeAsync(snapshot, tenantId, cancel: false, default);
    }

    private async Task<SubscriberMigration> LoadMigrationAsync(Guid migrationId)
    {
        await using var db = SubscriptionIntegrationDb.NewContext(_fits.TenantId);
        return await db
            .SubscriberMigrations.AsNoTracking()
            .Include(m => m.Offices)
            .SingleAsync(m => m.Id == migrationId);
    }

    private static async Task<TenantSubscription> LoadSubscriptionAsync(Guid tenantId)
    {
        await using var db = SubscriptionIntegrationDb.NewContext(tenantId);
        return await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
    }

    // Plan propio: edición 1 sin precio, 2 ($50, 10 usuarios) contratada y 3 ($60, 3 usuarios) destino.
    private async Task SeedPlanAndOfficesAsync()
    {
        var nowUtc = DateTime.UtcNow;
        _plan = PlanTestData.PublishedPlan($"mig{Guid.NewGuid():N}"[..12]);
        _from = PlanTestData.PublishNewEdition(_plan, 50m, seatsMax: 10);
        var offices = new[] { _fits.TenantId, _tooMany.TenantId }
            .Select(tenantId =>
                TenantSubscription
                    .ActivateImmediately(
                        tenantId,
                        _plan,
                        _from,
                        BillingCycle.Monthly,
                        nowUtc.AddDays(-1),
                        nowUtc.AddDays(29),
                        Guid.Empty,
                        nowUtc
                    )
                    .Value
            )
            .ToList();
        _to = PlanTestData.PublishNewEdition(_plan, 60m, seatsMax: 3);
        _plan.ClearDomainEvents();
        offices.ForEach(office => office.ClearDomainEvents());

        await using var db = SubscriptionIntegrationDb.NewContext(_fits.TenantId);
        db.Plans.Add(_plan);
        db.Subscriptions.AddRange(offices);
        await db.SaveChangesAsync();
    }

    private Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        // Auth: la oficina que entra tiene 2 usuarios; la otra, 5 (la edición nueva trae 3).
        var users = FakeTenantUserCountClient.PerTenant(
            new Dictionary<Guid, int> { [_fits.TenantId] = 2, [_tooMany.TenantId] = 5 }
        );
        return SubscriptionWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(PreviewSubscriberMigrationHandler));
                options.Discovery.IncludeType(typeof(StartSubscriberMigrationHandler));
                options.Discovery.IncludeType(typeof(SubscriptionVersionChangeScheduledDomainEventHandler));
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options.UseTaxVisionDomainEvents();
                options.UseTaxVisionDurableLocalQueues();
                options
                    .PublishMessage<TenantSubscriptionStatusChangedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options.PublishMessage<SubscriptionRenewalDueIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();

                var services = options.Services;
                services.AddScoped<ISubscriberMigrationRepository, SubscriberMigrationRepository>();
                services.AddScoped<ISubscriberMigrationReads, SubscriberMigrationReads>();
                services.AddScoped<IPlanSubscriptionsAcrossTenants, PlanSubscriptionsAcrossTenants>();
                services.AddScoped<ICatalogAuditLog, CatalogAuditLog>();
                services.AddScoped<SubscriberMigrationPlanner>();
                services.AddSingleton<ITenantUserCountClient>(users);
                services.AddSingleton<IStorageUsageClient>(new FakeStorageUsageClient());
                services.AddSingleton(sp => new SubscriberMigrationJob(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    lockFactory: null!,
                    NullLogger<SubscriberMigrationJob>.Instance
                ));
                services.AddSingleton(sp => new TenantSubscriptionRenewalJob(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    lockFactory: null!,
                    NullLogger<TenantSubscriptionRenewalJob>.Instance
                ));
            }
        );
    }

    private async Task<IReadOnlyList<string>> WaitForBodiesAsync(string messageType, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        IReadOnlyList<string> bodies = [];
        while (DateTime.UtcNow < deadline)
        {
            bodies = await OutgoingBodiesAsync(messageType);
            if (bodies.Count >= expected)
                return bodies;
            await Task.Delay(250);
        }
        return bodies;
    }

    private async Task<IReadOnlyList<string>> OutgoingBodiesAsync(string messageType)
    {
        await using var connection = new SqlConnection(SubscriptionIntegrationDb.Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[{_schema}].[wolverine_outgoing_envelopes]') IS NOT NULL "
            + $"SELECT body FROM [{_schema}].[wolverine_outgoing_envelopes] WHERE message_type LIKE '%{messageType}%'";
        var bodies = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            bodies.Add(Encoding.UTF8.GetString((byte[])reader[0]));
        return bodies;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
