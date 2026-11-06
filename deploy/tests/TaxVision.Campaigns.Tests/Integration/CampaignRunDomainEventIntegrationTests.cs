using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
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
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Consumers;
using TaxVision.Campaigns.Application.Runs.DomainEvents;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Infrastructure.Persistence;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;
using TaxVision.Campaigns.Tests.Fakes;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// CMP-10 y CMP-14 contra SQL Server local, en una base temporal propia: el Campaigns de la flota no ve estos
/// agendados ni estos sobres, aunque la prueba se aborte. Los avisos van a un puerto TCP cerrado y se quedan en
/// wolverine_outgoing_envelopes.
/// </summary>
public sealed class CampaignRunDomainEventIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "cmpbb1_";
    private const string EnvelopeSchema = "cmpbb1";

    private readonly string _connectionString = new SqlConnectionStringBuilder(CampaignsTestDatabase.ConnectionString)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _creator = Guid.NewGuid();
    private readonly FailAfterClosingInterceptor _interceptor = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using (var db = NewContext())
            await db.Database.EnsureCreatedAsync();
        try
        {
            _host = await StartHostAsync();
        }
        catch
        {
            // Si el host no arranca, xUnit no llama DisposeAsync: la base temporal se borra aquí.
            await DisposeAsync();
            throw;
        }
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

    /// <summary>El disparo guarda dos veces en la misma transacción: el arranque se anuncia una sola vez.</summary>
    [Fact]
    public async Task FireSchedule_AnnouncesTheStartOnce()
    {
        var runId = await FireAsync();

        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "CampaignRunStartedDomainEvent"));
        Assert.True(await OutgoingReachesAsync(nameof(CampaignRunStartedIntegrationEvent), 1));
        Assert.Equal(
            2,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(CampaignDispatchRequestedIntegrationEvent))
        );
        Assert.Equal(CampaignRunStatus.Dispatching, await StatusOfAsync(runId));
    }

    [Fact]
    public async Task LastResult_ClosesTheRunAndAnnouncesItOnce()
    {
        var runId = await FireAsync();
        var (first, second) = await DispatchIdsAsync(runId);

        await ApplyAsync(runId, first, "Delivered");
        await ApplyAsync(runId, second, "Delivered");

        Assert.Equal(CampaignRunStatus.Completed, await StatusOfAsync(runId));
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "CampaignRunClosedDomainEvent"));
        Assert.True(await OutgoingReachesAsync(nameof(CampaignRunCompletedIntegrationEvent), 1));
    }

    [Fact]
    public async Task LastResult_FailureAfterSave_LeavesTheRunOpenAndNoEnvelope()
    {
        var runId = await FireAsync();
        var (first, second) = await DispatchIdsAsync(runId);
        await ApplyAsync(runId, first, "Delivered");
        _interceptor.Fail = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ApplyAsync(runId, second, "Delivered"));

        Assert.Equal(CampaignRunStatus.Dispatching, await StatusOfAsync(runId));
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "CampaignRunClosedDomainEvent"));
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(CampaignRunCompletedIntegrationEvent))
        );
    }

    /// <summary>
    /// BB-7 en una cola durable: la espera del reintento vive en la base, así que un reinicio no pierde el
    /// resultado. Se mide durante la espera; es lo que confirma la advertencia de la guía.
    /// </summary>
    [Fact]
    public async Task ResultBeforeItsRun_WaitsInTheDurableInbox()
    {
        await _host
            .Services.GetRequiredService<IMessageBus>()
            .PublishAsync(Result(Guid.NewGuid(), "dispatch-1", "Delivered"));

        Assert.True(
            await WaitUntilAsync(async () =>
                await CountAsync(
                    $"SELECT COUNT(*) FROM [{EnvelopeSchema}].[wolverine_incoming_envelopes] "
                        + "WHERE message_type LIKE '%CampaignDispatchResultIntegrationEvent%' AND status = 'Scheduled'"
                ) == 1
            )
        );
    }

    private async Task<Guid> FireAsync()
    {
        var (schedule, lease, slot) = await SeedDueScheduleAsync();
        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CampaignRunResponse>>(
                new FireCampaignScheduleCommand(_tenantId, schedule.Id, lease, slot)
            );
        Assert.True(result.IsSuccess);
        return result.Value.Id;
    }

    private Task ApplyAsync(Guid runId, string dispatchId, string outcome) =>
        _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(Result(runId, dispatchId, outcome));

    private CampaignDispatchResultIntegrationEvent Result(Guid runId, string dispatchId, string outcome) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "test",
            CampaignId = Guid.NewGuid(),
            RunId = runId,
            DispatchId = dispatchId,
            Outcome = outcome,
        };

    private async Task<(string First, string Second)> DispatchIdsAsync(Guid runId)
    {
        await using var db = NewContext();
        var ids = await db.Set<CampaignRecipient>()
            .IgnoreQueryFilters()
            .Where(r => r.RunId == runId)
            .Select(r => r.DispatchId)
            .ToListAsync();
        return (ids[0], ids[1]);
    }

    private async Task<CampaignRunStatus> StatusOfAsync(Guid runId)
    {
        await using var db = NewContext();
        return await db.CampaignRuns.IgnoreQueryFilters().Where(r => r.Id == runId).Select(r => r.Status).SingleAsync();
    }

    private async Task<(CampaignSchedule Schedule, Guid Lease, DateTime Slot)> SeedDueScheduleAsync()
    {
        var campaign = Campaign.Create(_tenantId, _creator, "Newsletter", CampaignChannel.Email, "body", "subj").Value;
        var list = ContactList.Create(_tenantId, "L", null).Value;
        var ana = Contact.Create(_tenantId, null, $"ana-{Guid.NewGuid():N}@x.com", null, ContactSource.Manual).Value;
        var beto = Contact.Create(_tenantId, null, $"beto-{Guid.NewGuid():N}@x.com", null, ContactSource.Manual).Value;
        list.AddMember(ana.Id);
        list.AddMember(beto.Id);

        // Precisión de datetime2: el slot que vuelve de SQL es el mismo que se compara en HoldsLease.
        var slot = DateTime.UtcNow.AddMinutes(-1);
        slot = new DateTime(slot.Ticks - slot.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var schedule = CampaignSchedule
            .CreateRecurring(
                _tenantId,
                campaign.Id,
                slot,
                RecurrenceFrequency.Hourly,
                null,
                [list.Id],
                createdByUserId: _creator
            )
            .Value;
        var lease = ScheduleLease.Take(schedule, DateTime.UtcNow.AddMinutes(2));

        await using var seed = NewContext();
        seed.Campaigns.Add(campaign);
        seed.Contacts.AddRange(ana, beto);
        seed.ContactLists.Add(list);
        seed.CampaignSchedules.Add(schedule);
        await seed.SaveChangesAsync();
        return (schedule, lease, slot);
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                services.AddDbContext<CampaignsDbContext>(options =>
                    options.UseSqlServer(_connectionString).AddInterceptors(_interceptor)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CampaignsDbContext>());
                services.AddSingleton<ITenantContext>(new CampaignsTestDatabase.FixedTenant(_tenantId));
                services.AddScoped<ICampaignRepository, CampaignRepository>();
                services.AddScoped<ICampaignRunRepository, CampaignRunRepository>();
                services.AddScoped<ICampaignScheduleRepository, CampaignScheduleRepository>();
                services.AddScoped<IContactRepository, ContactRepository>();
                services.AddScoped<IContactListRepository, ContactListRepository>();
                services.AddScoped<ISenderProfileRepository, SenderProfileRepository>();
                services.AddSingleton<ICustomerAudienceClient>(new FakeCustomerAudienceClient());
                services.AddSingleton<ICampaignCustomerAssignmentReader>(new FakeCampaignCustomerAssignmentReader());
                services.AddSingleton<IWalletSpendClient>(new FakeWalletSpendClient());
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            })
            .UseWolverine(options =>
            {
                options.ServiceName = "cmp-bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(FireCampaignScheduleHandler))
                    .IncludeType(typeof(ApplyDispatchResultConsumer))
                    .IncludeType(typeof(CampaignRunStartedDomainEventHandler))
                    .IncludeType(typeof(CampaignRunClosedDomainEventHandler));
                options.PersistMessagesWithSqlServer(_connectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CampaignsDbContext>();
                options.Policies.AutoApplyTransactions();

                // Espera larga: el mensaje se queda en Scheduled mientras la prueba lo mira.
                options.ApplyStandardFailurePolicies(
                    new DependencyNotReadyRetryOptions { Delays = [TimeSpan.FromHours(1)] }
                );

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options
                    .PublishMessage<CampaignDispatchRequestedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options.PublishMessage<CampaignRunStartedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<CampaignRunCompletedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task<bool> OutgoingReachesAsync(string messageType, int expected) =>
        WaitUntilAsync(async () => await CountRowsAsync("wolverine_outgoing_envelopes", messageType) == expected);

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

    private Task<int> CountRowsAsync(string table, string messageType) =>
        CountAsync(
            $"IF OBJECT_ID('[{EnvelopeSchema}].[{table}]') IS NULL SELECT 0 ELSE "
                + $"SELECT COUNT(*) FROM [{EnvelopeSchema}].[{table}] WHERE message_type LIKE '%{messageType}%'"
        );

    private async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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

    private CampaignsDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CampaignsDbContext>().UseSqlServer(_connectionString).Options,
            new CampaignsTestDatabase.FixedTenant(_tenantId)
        );

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Falla después de guardar el cierre y antes de confirmar: la transacción de Wolverine deshace fila y sobre.
    private sealed class FailAfterClosingInterceptor : SaveChangesInterceptor
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
                && eventData
                    .Context!.ChangeTracker.Entries<CampaignRun>()
                    .Any(e => e.Entity.Status != CampaignRunStatus.Dispatching);
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
