using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Jobs;
using TaxVision.Calendar.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// CAL-3 con SQL real. El aviso va a un puerto que escucha: lo que se mide es lo que llega de verdad, porque el sobre
/// de una unidad revertida no queda en la base y contar filas daría falso verde.
/// </summary>
public sealed class StartingSoonDedupIntegrationTests : IAsyncLifetime
{
    private const string EnvelopeSchema = "calstart";

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private readonly ReceivedNotices _received = new();
    private readonly RivalNoticeInterceptor _rival = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await StartHostAsync();
        }
        catch
        {
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
        await _database.DropAsync();
    }

    /// <summary>Dos instancias del job, sin memoria compartida: dos réplicas, o un reinicio.</summary>
    [Fact(Timeout = 120_000)]
    public async Task TwoRunsSameOccurrence_PublishOnce()
    {
        var appointmentId = await SeedAsync(DateTime.UtcNow.AddMinutes(10));

        await NewJob().NotifyAsync(CancellationToken.None);
        await NewJob().NotifyAsync(CancellationToken.None);

        Assert.Equal(1, await _received.SettledCountAsync(appointmentId));
        await using var read = _database.NewContext();
        Assert.Equal(1, await read.StartingSoonNotices.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>La otra réplica gana entre la comprobación y el INSERT: el índice decide y aquí no sale nada.</summary>
    [Fact(Timeout = 120_000)]
    public async Task OtherReplicaWinsTheInsert_NothingIsSentAndTheNextUnitGoesOn()
    {
        var start = TruncateToSeconds(DateTime.UtcNow.AddMinutes(10));
        var lost = await SeedAsync(start);
        var won = await SeedAsync(start.AddMinutes(1));
        _rival.BeforeInsert = async () =>
        {
            await using var db = _database.NewContext();
            db.StartingSoonNotices.Add(
                StartingSoonNotice.Create(_tenantId, OccurrenceTargetId.For(lost, start), start, DateTime.UtcNow)
            );
            await db.SaveChangesAsync();
        };

        await NewJob().NotifyAsync(CancellationToken.None);

        Assert.Equal(1, await _received.SettledCountAsync(won));
        Assert.Equal(0, _received.CountFor(lost));
        await using var read = _database.NewContext();
        Assert.Equal(2, await read.StartingSoonNotices.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Si la ocurrencia se movió, la marca vieja no apaga el aviso de la hora nueva.</summary>
    [Fact(Timeout = 120_000)]
    public async Task MovedOccurrence_IsAnnouncedAgain()
    {
        var start = TruncateToSeconds(DateTime.UtcNow.AddMinutes(10));
        var appointmentId = await SeedAsync(start);
        await using (var db = _database.NewContext())
        {
            db.StartingSoonNotices.Add(
                StartingSoonNotice.Create(
                    _tenantId,
                    OccurrenceTargetId.For(appointmentId, start),
                    start.AddMinutes(-30),
                    DateTime.UtcNow
                )
            );
            await db.SaveChangesAsync();
        }

        await NewJob().NotifyAsync(CancellationToken.None);

        Assert.Equal(1, await _received.SettledCountAsync(appointmentId));
    }

    [Fact(Timeout = 120_000)]
    public async Task Cleanup_RemovesOnlyNoticesPastTheRetention()
    {
        var now = DateTime.UtcNow;
        await using (var db = _database.NewContext())
        {
            db.StartingSoonNotices.Add(StartingSoonNotice.Create(_tenantId, Guid.NewGuid(), now, now.AddDays(-8)));
            db.StartingSoonNotices.Add(StartingSoonNotice.Create(_tenantId, Guid.NewGuid(), now, now.AddDays(-6)));
            await db.SaveChangesAsync();
        }
        var job = new CalendarRetentionJob(
            _host!.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new StartingSoonOptions()),
            NullLogger<CalendarRetentionJob>.Instance
        );

        await using (var db = _database.NewContext())
            await job.PurgeNoticesAsync(db, CancellationToken.None);

        await using var read = _database.NewContext();
        var left = await read.StartingSoonNotices.IgnoreQueryFilters().SingleAsync();
        Assert.True(left.PublishedAtUtc > now.AddDays(-7));
    }

    private StartingSoonJob NewJob() =>
        new(
            _host!.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new StartingSoonOptions()),
            NullLogger<StartingSoonJob>.Instance
        );

    private async Task<Guid> SeedAsync(DateTime startUtc)
    {
        var timing = EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), "America/New_York").Value;
        var appointment = Appointment
            .Schedule(
                _tenantId,
                AppointmentTitle.Create("Client review").Value,
                timing,
                Guid.NewGuid(),
                _organizer,
                DateTime.UtcNow
            )
            .Value;
        appointment.AddAttendee(
            AttendeeKind.InternalUser,
            Guid.NewGuid(),
            null,
            AttendeeSnapshot.Create("Staff", "staff@firm.test").Value,
            false,
            _organizer,
            DateTime.UtcNow,
            false
        );

        await using var db = _database.NewContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<IHost> StartHostAsync()
    {
        var port = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            // El temporizador de Wolverine puede registrar tras el Dispose: EventLog lanzaría y tumbaría el proceso.
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ServiceName = "cal-3-starting-soon-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ReceivedNoticeHandler));
                options.PersistMessagesWithSqlServer(_database.ConnectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                options.Services.AddDbContext<CalendarDbContext>(db =>
                    db.UseSqlServer(_database.ConnectionString).AddInterceptors(_rival)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
                options.Services.AddSingleton<ITenantContext, CalendarTemporaryDatabase.NoTenantContext>();
                options.Services.AddSingleton(_received);
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CalendarDbContext>();

                options.ListenAtPort(port);
                options.PublishMessage<AppointmentStartingSoonIntegrationEvent>().ToPort(port).UseDurableOutbox();
            })
            .StartAsync();
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Guarda la marca de la otra réplica justo antes del INSERT del job: un choque real con el índice.</summary>
    private sealed class RivalNoticeInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeInsert { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var inserting = eventData
                .Context!.ChangeTracker.Entries<StartingSoonNotice>()
                .Any(entry => entry.State == EntityState.Added);
            if (BeforeInsert is { } rival && inserting)
            {
                BeforeInsert = null;
                await rival();
            }
            return result;
        }
    }
}

/// <summary>Lo que de verdad llegó al puerto.</summary>
public sealed class ReceivedNotices
{
    private readonly ConcurrentQueue<Guid> _appointmentIds = new();

    public void Add(Guid appointmentId) => _appointmentIds.Enqueue(appointmentId);

    public int CountFor(Guid appointmentId) => _appointmentIds.Count(id => id == appointmentId);

    /// <summary>Espera el primero y da tiempo a que aparezca uno de más.</summary>
    public async Task<int> SettledCountAsync(Guid appointmentId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (CountFor(appointmentId) == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        await Task.Delay(TimeSpan.FromSeconds(3));
        return CountFor(appointmentId);
    }
}

public static class ReceivedNoticeHandler
{
    public static void Handle(AppointmentStartingSoonIntegrationEvent evt, ReceivedNotices received) =>
        received.Add(evt.AppointmentId);
}
