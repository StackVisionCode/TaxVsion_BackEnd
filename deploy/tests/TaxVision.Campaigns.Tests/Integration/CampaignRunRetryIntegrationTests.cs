using System.Collections.Concurrent;
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
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Runs.DomainEvents;
using TaxVision.Campaigns.Application.Scheduling;
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
/// Reintentos del arranque de un run contra SQL Server local, en una base temporal propia y con las políticas
/// reales: un Wallet caído no consume el slot, y un guardado que falla no duplica el envío masivo. Los avisos de
/// envío llegan a un sumidero en una cola local durable, así se cuentan los que de verdad salen.
/// </summary>
public sealed class CampaignRunRetryIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "cmprtry_";
    private const string EnvelopeSchema = "cmprtry";

    private readonly string _connectionString = new SqlConnectionStringBuilder(
        CampaignsTestDatabase.ServerConnectionString
    )
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _creator = Guid.NewGuid();
    private readonly FailOnceInterceptor _interceptor = new();
    private readonly FakeWalletSpendClient _wallet = new();
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

    [Fact]
    public async Task WalletUnavailable_KeepsTheSlotAndNextClaimFiresItOnce()
    {
        var (schedule, lease, slot) = await SeedDueScheduleAsync();
        _wallet.ReserveResult = WalletReserveResult.Unreachable();

        var failed = await FireAsync(schedule.Id, lease, slot);

        Assert.Equal(CampaignRunErrors.WalletUnavailable, failed.Error);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns WHERE Status = 0"));
        var pending = await ReadScheduleAsync(schedule.Id);
        Assert.Equal(ScheduleStatus.Active, pending.Status);
        Assert.Equal(slot, pending.NextFireAtUtc);
        Assert.Equal(0, pending.OccurrenceCount);

        // El lease vence y el scheduler vuelve a reclamar el mismo slot.
        _wallet.ReserveResult = new WalletReserveResult(true, true, 0, 0, 0, "USD");
        await ExpireLeaseAsync(schedule.Id);
        var claimed = Assert.Single(await ClaimDueAsync());
        Assert.Equal(slot, claimed.NextFireAtUtc);
        var fired = await FireAsync(schedule.Id, claimed.LeaseToken, claimed.NextFireAtUtc);

        Assert.True(fired.IsSuccess);
        Assert.Equal(ScheduledRunId.For(schedule.Id, slot), fired.Value.Id);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns"));
        Assert.Single(_wallet.ReservedReferences.Distinct());
        Assert.Equal(2, await DispatchesReachingSinkAsync(expected: 2));
        var done = await ReadScheduleAsync(schedule.Id);
        Assert.Equal(ScheduleStatus.Completed, done.Status);
        Assert.Equal(1, done.OccurrenceCount);
    }

    [Fact]
    public async Task Fire_SaveFailsOnce_DoesNotDuplicateTheDispatches()
    {
        var (schedule, lease, slot) = await SeedDueScheduleAsync();
        _interceptor.FailWhen = context =>
            context.ChangeTracker.Entries<CampaignSchedule>().Any(e => e.State == EntityState.Modified);

        var result = await FireAsync(schedule.Id, lease, slot);

        Assert.True(result.IsSuccess);
        Assert.True(_interceptor.Triggered);
        Assert.Equal(2, await DispatchesReachingSinkAsync(expected: 2));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns"));
    }

    [Fact]
    public async Task ManualStart_SaveFailsOnce_DoesNotDuplicateTheDispatches()
    {
        var campaign = await SeedCampaignAsync();
        _interceptor.FailWhen = context => context.ChangeTracker.Entries<CampaignRun>().Any();

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CampaignRunResponse>>(
                new StartCampaignRunCommand(
                    _tenantId,
                    campaign.Id,
                    _creator,
                    [new StartRunRecipient("c1", "ana@x.com", null), new StartRunRecipient("c2", "beto@x.com", null)]
                )
            );

        Assert.True(result.IsSuccess);
        Assert.True(_interceptor.Triggered);
        Assert.Equal(2, await DispatchesReachingSinkAsync(expected: 2));
    }

    // CMP-05 (BB-8): el Wallet ya reservó y el guardado falla. El reintento no puede reservar con otra referencia.
    [Fact]
    public async Task FailureAfterWalletAccepted_DoesNotReserveTwice()
    {
        var campaign = await SeedCampaignAsync();
        _interceptor.FailWhen = context =>
            context.ChangeTracker.Entries<CampaignRun>().Any(e => e.Entity.Status == CampaignRunStatus.Dispatching);

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CampaignRunResponse>>(
                new StartCampaignRunCommand(
                    _tenantId,
                    campaign.Id,
                    _creator,
                    [new StartRunRecipient("c1", "ana@x.com", null), new StartRunRecipient("c2", "beto@x.com", null)]
                )
            );

        Assert.True(result.IsSuccess);
        Assert.True(_interceptor.Triggered);
        Assert.Single(_wallet.ReservedReferences.Distinct());
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns"));
        Assert.Equal(2, await DispatchesReachingSinkAsync(expected: 2));
    }

    // El guardado sigue fallando: el run queda en Created y el barrido lo retoma con la misma referencia.
    [Fact]
    public async Task FailureAfterWalletAccepted_SweepResumesWithTheSameReference()
    {
        var campaign = await SeedCampaignAsync();
        _interceptor.FailTimes = int.MaxValue;
        _interceptor.FailWhen = context =>
            context.ChangeTracker.Entries<CampaignRun>().Any(e => e.Entity.Status == CampaignRunStatus.Dispatching);
        var bus = _host.Services.GetRequiredService<IMessageBus>();

        var result = await bus.InvokeAsync<Result<CampaignRunResponse>>(
            new StartCampaignRunCommand(
                _tenantId,
                campaign.Id,
                _creator,
                [new StartRunRecipient("c1", "ana@x.com", null), new StartRunRecipient("c2", "beto@x.com", null)]
            )
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(CampaignRunStatus.Created), result.Value.Status);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns WHERE Status = 0"));

        _interceptor.FailWhen = null;
        await using (var db = NewContext())
            Assert.Equal(
                result.Value.Id,
                Assert
                    .Single(
                        await new CampaignRunRepository(db).ListCreatedBeforeAsync(DateTime.UtcNow.AddMinutes(1), 10)
                    )
                    .Id
            );
        await bus.InvokeAsync(new ResumeCreatedCampaignRunCommand(_tenantId, result.Value.Id));

        Assert.Single(_wallet.ReservedReferences.Distinct());
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns WHERE Status = 1"));
        Assert.Equal(2, await DispatchesReachingSinkAsync(expected: 2));
    }

    [Fact]
    public async Task ManualStart_WalletDown_RejectsAndReleasesThroughTheDurableQueue()
    {
        var campaign = await SeedCampaignAsync();
        _wallet.ReserveResult = WalletReserveResult.Unreachable();

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CampaignRunResponse>>(
                new StartCampaignRunCommand(
                    _tenantId,
                    campaign.Id,
                    _creator,
                    [new StartRunRecipient("c1", "ana@x.com", null)]
                )
            );

        Assert.Equal(CampaignRunErrors.WalletUnavailable, result.Error);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM CampaignRuns WHERE Status = 6"));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (_wallet.Settlements.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        Assert.Equal(0, Assert.Single(_wallet.Settlements).ConsumedUnits);
    }

    private Task<Result<CampaignRunResponse>> FireAsync(Guid scheduleId, Guid lease, DateTime slot) =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CampaignRunResponse>>(
                new FireCampaignScheduleCommand(_tenantId, scheduleId, lease, slot)
            );

    // Espera lo esperado y un margen más: un sobre filtrado llega después del primero.
    private async Task<int> DispatchesReachingSinkAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Sink.ForTenant(_tenantId).Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(200);
        await Task.Delay(TimeSpan.FromSeconds(3));
        return Sink.ForTenant(_tenantId).Count;
    }

    private async Task<CampaignSchedule> ReadScheduleAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.CampaignSchedules.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<IReadOnlyList<ClaimedSchedule>> ClaimDueAsync()
    {
        await using var db = NewContext();
        return await new CampaignScheduleRepository(db).ClaimDueAsync(DateTime.UtcNow, 120, 20);
    }

    private async Task ExpireLeaseAsync(Guid scheduleId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE CampaignSchedules SET LeasedUntilUtc = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE Id = @id";
        command.Parameters.AddWithValue("@id", scheduleId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Campaign> SeedCampaignAsync()
    {
        var campaign = Campaign.Create(_tenantId, _creator, "Newsletter", CampaignChannel.Email, "body", "subj").Value;
        await using var seed = NewContext();
        seed.Campaigns.Add(campaign);
        await seed.SaveChangesAsync();
        return campaign;
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
            .CreateOneTime(_tenantId, campaign.Id, slot, [list.Id], createdByUserId: _creator)
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

    private async Task<IHost> StartHostAsync() =>
        await Host.CreateDefaultBuilder()
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
                services.AddCampaignRunOrchestration();
                services.AddScoped<IContactRepository, ContactRepository>();
                services.AddScoped<IContactListRepository, ContactListRepository>();
                services.AddScoped<ISenderProfileRepository, SenderProfileRepository>();
                services.AddSingleton<ICustomerAudienceClient>(new FakeCustomerAudienceClient());
                services.AddSingleton<ICampaignCustomerAssignmentReader>(new FakeCampaignCustomerAssignmentReader());
                services.AddSingleton<IWalletSpendClient>(_wallet);
                services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
                services.AddSingleton(Options.Create(new CampaignSchedulerOptions()));
            })
            .UseWolverine(options =>
            {
                options.ServiceName = "cmp-run-retry-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(FireCampaignScheduleHandler))
                    .IncludeType(typeof(SkipScheduledSlotHandler))
                    .IncludeType(typeof(StartCampaignRunHandler))
                    .IncludeType(typeof(OpenCampaignRunHandler))
                    .IncludeType(typeof(ApplyCampaignRunFundingHandler))
                    .IncludeType(typeof(ResumeCreatedCampaignRunHandler))
                    .IncludeType(typeof(ReleaseCampaignRunReservationHandler))
                    .IncludeType(typeof(CampaignRunStartedDomainEventHandler))
                    .IncludeType(typeof(CampaignRunClosedDomainEventHandler))
                    .IncludeType(typeof(SinkHandler));
                options.PersistMessagesWithSqlServer(_connectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CampaignsDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options.PublishMessage<CampaignDispatchRequestedIntegrationEvent>().ToLocalQueue("cmp-retry-sink");
                options.PublishMessage<CampaignRunStartedIntegrationEvent>().ToLocalQueue("cmp-retry-other");
                options.PublishMessage<CampaignRunCompletedIntegrationEvent>().ToLocalQueue("cmp-retry-other");
            })
            .StartAsync();

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

    /// <summary>Hace fallar una sola vez el guardado que cumple la condición.</summary>
    private sealed class FailOnceInterceptor : SaveChangesInterceptor
    {
        private int _fired;

        public Func<DbContext, bool>? FailWhen { get; set; }
        public int FailTimes { get; set; } = 1;
        public bool Triggered => _fired > 0;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (FailWhen?.Invoke(eventData.Context!) == true && Interlocked.Increment(ref _fired) <= FailTimes)
                throw new InvalidOperationException("Simulated save failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Sumidero: registra por tenant lo que salió por el bus.</summary>
    public static class Sink
    {
        private static readonly ConcurrentDictionary<Guid, ConcurrentBag<object>> Received = new();

        public static void Add(Guid tenantId, object message) => Received.GetOrAdd(tenantId, _ => []).Add(message);

        public static IReadOnlyList<object> ForTenant(Guid tenantId) =>
            Received.TryGetValue(tenantId, out var bag) ? bag.ToList() : [];
    }

    public static class SinkHandler
    {
        public static void Handle(CampaignDispatchRequestedIntegrationEvent message) =>
            Sink.Add(message.TenantId, message);

        public static void Handle(CampaignRunStartedIntegrationEvent message) { }

        public static void Handle(CampaignRunCompletedIntegrationEvent message) { }
    }
}
