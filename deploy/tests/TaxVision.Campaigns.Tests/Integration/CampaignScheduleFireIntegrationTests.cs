using System.Collections.Concurrent;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
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

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// CMP-01 contra el SQL Server local: el disparo del agendado (run + unidades + avance + sobres) se confirma
/// en una sola transacción de Wolverine. Los sobres van a un esquema propio y a una cola local que hace de
/// sumidero (sin RabbitMQ).
/// </summary>
public sealed class CampaignScheduleFireIntegrationTests : IAsyncLifetime
{
    private const string Schema = "cmp01_fire_test";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _creator = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await CampaignsTestDatabase.DeleteTenantAsync(_tenantId);
        await CampaignsTestDatabase.ExecuteAsync(
            $"IF OBJECT_ID('{Schema}.wolverine_incoming_envelopes') IS NOT NULL DELETE FROM {Schema}.wolverine_incoming_envelopes; "
                + $"IF OBJECT_ID('{Schema}.wolverine_outgoing_envelopes') IS NOT NULL DELETE FROM {Schema}.wolverine_outgoing_envelopes; "
                + $"IF OBJECT_ID('{Schema}.wolverine_dead_letters') IS NOT NULL DELETE FROM {Schema}.wolverine_dead_letters;"
        );
    }

    [Fact]
    public async Task Fire_FailureAfterSave_LeavesNoRunNoEnvelopeAndScheduleUnchanged()
    {
        var (schedule, lease, slot) = await SeedDueScheduleAsync();
        // Falla el guardado del avance, cuando el run y sus unidades ya se escribieron en la transacción.
        var crash = new FailWhenScheduleIsSaved();

        using var host = await StartHostAsync(crash);
        var bus = host.Services.GetRequiredService<IMessageBus>();

        var error = await Record.ExceptionAsync(() =>
            bus.InvokeAsync<Result<CampaignRunResponse>>(
                new FireCampaignScheduleCommand(_tenantId, schedule.Id, lease, slot)
            )
        );

        Assert.IsType<SimulatedCrashException>(error);
        Assert.True(crash.Triggered);
        await Task.Delay(TimeSpan.FromSeconds(2)); // margen por si un sobre llegara al sumidero

        Assert.Equal(
            0,
            await CampaignsTestDatabase.ScalarAsync(
                "SELECT COUNT(*) FROM CampaignRuns WHERE ScheduleId = @s",
                ("@s", schedule.Id)
            )
        );
        Assert.Equal(0, await CountEnvelopesAsync());
        Assert.Empty(Sink.ForTenant(_tenantId));

        await using var read = CampaignsTestDatabase.NewContext(_tenantId);
        var stored = await read.CampaignSchedules.AsNoTracking().SingleAsync(s => s.Id == schedule.Id);
        Assert.Equal(slot, stored.NextFireAtUtc);
        Assert.Null(stored.ActiveRunId);
        Assert.Equal(0, stored.OccurrenceCount);

        await host.StopAsync();
    }

    [Fact]
    public async Task Fire_Success_CommitsRunAdvanceAndEnvelopesTogether()
    {
        var (schedule, lease, slot) = await SeedDueScheduleAsync();

        using var host = await StartHostAsync(new FailWhenScheduleIsSaved { Enabled = false });
        var bus = host.Services.GetRequiredService<IMessageBus>();

        var result = await bus.InvokeAsync<Result<CampaignRunResponse>>(
            new FireCampaignScheduleCommand(_tenantId, schedule.Id, lease, slot)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ScheduledRunId.For(schedule.Id, slot), result.Value.Id);
        await WaitUntilAsync(() => Sink.ForTenant(_tenantId).OfType<CampaignDispatchRequestedIntegrationEvent>().Any());

        await using var read = CampaignsTestDatabase.NewContext(_tenantId);
        var stored = await read.CampaignSchedules.AsNoTracking().SingleAsync(s => s.Id == schedule.Id);
        Assert.Equal(result.Value.Id, stored.ActiveRunId);
        Assert.True(stored.NextFireAtUtc > slot);
        Assert.Null(stored.LeaseToken);

        await host.StopAsync();
    }

    [Fact]
    public async Task SameScheduleSlot_SecondRunInsert_ViolatesUniqueIndex()
    {
        var scheduleId = Guid.NewGuid();
        var slot = new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);
        var campaignId = Guid.NewGuid();

        await using (var first = CampaignsTestDatabase.NewContext(_tenantId))
        {
            first.CampaignRuns.Add(
                CampaignRun
                    .StartScheduled(
                        _tenantId,
                        campaignId,
                        _creator,
                        scheduleId,
                        slot,
                        [new RunRecipientDraft("c1", CampaignChannel.Email, "a@x.com", null)]
                    )
                    .Value
            );
            await first.SaveChangesAsync();
        }

        // Otro proceso inserta un run distinto (otro Id) para el mismo (agendado, slot).
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            CampaignsTestDatabase.ExecuteAsync(
                "INSERT INTO CampaignRuns (Id, TenantId, CampaignId, TriggeredByUserId, TriggerKind, CreatedAtUtc, "
                    + "Status, RecipientCount, CounterDispatched, CounterAccepted, CounterDelivered, CounterFailed, "
                    + "CounterSkipped, CounterUnknown, ScheduleId, ScheduledSlotUtc) "
                    + "VALUES (NEWID(), @t, @c, @u, 'Scheduled', SYSUTCDATETIME(), 1, 0, 0, 0, 0, 0, 0, 0, @s, @slot)",
                ("@t", _tenantId),
                ("@c", campaignId),
                ("@u", _creator),
                ("@s", scheduleId),
                ("@slot", slot)
            )
        );

        Assert.Equal(2601, error.Number);
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

        await using var seed = CampaignsTestDatabase.NewContext(_tenantId);
        seed.Campaigns.Add(campaign);
        seed.Contacts.AddRange(ana, beto);
        seed.ContactLists.Add(list);
        seed.CampaignSchedules.Add(schedule);
        await seed.SaveChangesAsync();
        return (schedule, lease, slot);
    }

    private async Task<IHost> StartHostAsync(FailWhenScheduleIsSaved crash) =>
        await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<CampaignsDbContext>(options =>
                    options.UseSqlServer(CampaignsTestDatabase.ConnectionString).AddInterceptors(crash)
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
                options.ServiceName = "cmp01-fire-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(FireCampaignScheduleHandler))
                    .IncludeType(typeof(SinkHandler));
                options.PersistMessagesWithSqlServer(CampaignsTestDatabase.ConnectionString, Schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CampaignsDbContext>();
                options.Policies.AutoApplyTransactions();
                options
                    .PublishMessage<CampaignDispatchRequestedIntegrationEvent>()
                    .ToLocalQueue("cmp01-sink")
                    .UseDurableInbox();
                options.PublishMessage<CampaignRunStartedIntegrationEvent>().ToLocalQueue("cmp01-sink").UseDurableInbox();
                options
                    .PublishMessage<CampaignRunCompletedIntegrationEvent>()
                    .ToLocalQueue("cmp01-sink")
                    .UseDurableInbox();
            })
            .StartAsync();

    private Task<int> CountEnvelopesAsync() =>
        CampaignsTestDatabase.ScalarAsync(
            $"SELECT (SELECT COUNT(*) FROM {Schema}.wolverine_incoming_envelopes) + (SELECT COUNT(*) FROM {Schema}.wolverine_outgoing_envelopes)"
        );

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

    public sealed class SimulatedCrashException() : Exception("Simulated crash after the run was saved.");

    /// <summary>Lanza en el SaveChanges que lleva el avance del agendado (después del guardado del run).</summary>
    public sealed class FailWhenScheduleIsSaved : SaveChangesInterceptor
    {
        public bool Enabled { get; init; } = true;
        public bool Triggered { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Enabled
                && eventData.Context!.ChangeTracker.Entries<CampaignSchedule>().Any(e => e.State == EntityState.Modified)
            )
            {
                Triggered = true;
                throw new SimulatedCrashException();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Sumidero: registra lo que salió del outbox por tenant.</summary>
    public static class Sink
    {
        private static readonly ConcurrentDictionary<Guid, ConcurrentBag<object>> Received = new();

        public static void Add(Guid tenantId, object message) =>
            Received.GetOrAdd(tenantId, _ => []).Add(message);

        public static IReadOnlyList<object> ForTenant(Guid tenantId) =>
            Received.TryGetValue(tenantId, out var bag) ? bag.ToList() : [];
    }

    public static class SinkHandler
    {
        public static void Handle(CampaignDispatchRequestedIntegrationEvent message) => Sink.Add(message.TenantId, message);

        public static void Handle(CampaignRunStartedIntegrationEvent message) => Sink.Add(message.TenantId, message);

        public static void Handle(CampaignRunCompletedIntegrationEvent message) => Sink.Add(message.TenantId, message);
    }
}
