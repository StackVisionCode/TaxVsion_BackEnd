using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Jobs;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// CAL-BB1 y CAL-BB3 contra SQL Server local, en una base temporal propia: el host de la flota nunca ve estas
/// citas ni estos sobres, aunque la prueba se aborte. Los avisos van a un puerto TCP cerrado y se quedan en
/// wolverine_outgoing_envelopes.
/// </summary>
public sealed class AppointmentDomainEventIntegrationTests : IAsyncLifetime
{
    private const string ApiUserSecretsId = "b7f4a1d2-9c3e-4a58-8f21-6d0e5b3c74a9";
    private const string Prefix = "calbb1_";
    private const string EnvelopeSchema = "calbb1";

    private readonly string _connectionString;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private readonly FailAfterCancelInterceptor _interceptor = new();
    private readonly HoldingGate _gate = new();
    private IHost _host = null!;

    public AppointmentDomainEventIntegrationTests()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("CALENDAR_DB_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).Build().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set CALENDAR_DB_CONNECTION or the Calendar user-secrets.");
        _connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;
    }

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
        _gate.Release();
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
    public async Task CancelAppointment_EnvelopeStoredWithRow()
    {
        var appointmentId = await SeedAsync(DateTime.UtcNow.AddDays(3));

        var result = await CancelAsync(appointmentId);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, await StatusOfAsync(appointmentId));
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "AppointmentCancelledDomainEvent"));
        Assert.True(await OutgoingReachesAsync(nameof(AppointmentCancelledIntegrationEvent), 1));
        Assert.True(await OutgoingReachesAsync(nameof(ReminderTargetClosedIntegrationEvent), 1));
    }

    [Fact]
    public async Task CancelAppointment_FailureAfterSave_NoChangeNoEnvelope()
    {
        var appointmentId = await SeedAsync(DateTime.UtcNow.AddDays(3));
        _interceptor.Fail = true;

        await Assert.ThrowsAnyAsync<Exception>(() => CancelAsync(appointmentId));

        Assert.Equal(AppointmentStatus.Confirmed, await StatusOfAsync(appointmentId));
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "AppointmentCancelledDomainEvent"));
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(AppointmentCancelledIntegrationEvent))
        );
    }

    /// <summary>
    /// Se mide a medio procesar: con el handler de dominio retenido, el evento ya está guardado y sin atender. Es
    /// lo que sobrevive a un reinicio; contar filas después de procesar no lo prueba.
    /// </summary>
    [Fact]
    public async Task DomainEvent_WaitsInTheDurableInboxWhileItsHandlerIsHeld()
    {
        var appointmentId = await SeedAsync(DateTime.UtcNow.AddDays(3));
        _gate.HoldNextHandlerRead();

        await CancelAsync(appointmentId);

        Assert.True(await _gate.ReachedAsync());
        Assert.Equal(
            1,
            await CountAsync(
                $"SELECT COUNT(*) FROM [{EnvelopeSchema}].[wolverine_incoming_envelopes] "
                    + "WHERE message_type LIKE '%AppointmentCancelledDomainEvent%' AND status <> 'Handled'"
            )
        );
        Assert.Equal(
            0,
            await CountRowsAsync("wolverine_outgoing_envelopes", nameof(AppointmentCancelledIntegrationEvent))
        );

        _gate.Release();

        Assert.True(await OutgoingReachesAsync(nameof(AppointmentCancelledIntegrationEvent), 1));
    }

    [Fact]
    public async Task StartingSoonJob_EachNoticeGoesThroughTheOutboxOfItsUnit()
    {
        await SeedAsync(DateTime.UtcNow.AddMinutes(17), attendee: Guid.NewGuid());
        await SeedAsync(DateTime.UtcNow.AddMinutes(17), attendee: Guid.NewGuid());

        var job = new StartingSoonJob(
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new StartingSoonOptions()),
            NullLogger<StartingSoonJob>.Instance
        );
        await job.StartAsync(CancellationToken.None);

        Assert.True(await OutgoingReachesAsync(nameof(AppointmentStartingSoonIntegrationEvent), 2));
        await job.StopAsync(CancellationToken.None);
    }

    private Task<Result> CancelAsync(Guid appointmentId) =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(
                new CancelAppointmentCommand(_tenantId, appointmentId, _organizer, null, null, "test", false)
            );

    private async Task<Guid> SeedAsync(DateTime startUtc, Guid? attendee = null)
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
        appointment.RequestReminder(30);
        if (attendee is { } userId)
            appointment.AddAttendee(
                AttendeeKind.InternalUser,
                userId,
                null,
                AttendeeSnapshot.Create("Staff", "staff@firm.test").Value,
                false,
                _organizer,
                DateTime.UtcNow,
                false
            );

        await using var db = NewContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<AppointmentStatus> StatusOfAsync(Guid appointmentId)
    {
        await using var db = NewContext();
        return await db
            .Appointments.IgnoreQueryFilters()
            .Where(a => a.Id == appointmentId)
            .Select(a => a.Status)
            .SingleAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            // El temporizador de Wolverine puede registrar tras el Dispose: EventLog lanzaría y tumbaría el proceso.
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ServiceName = "cal-bb1-domain-events-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CancelAppointmentHandler))
                    .IncludeType(typeof(AppointmentCancelledDomainEventHandler))
                    .IncludeType(typeof(AppointmentReminderTargetsClosedDomainEventHandler));
                options.PersistMessagesWithSqlServer(_connectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<CalendarDbContext>(db =>
                    db.UseSqlServer(_connectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
                options.Services.AddSingleton<ITenantContext, NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext, FixedCorrelation>();
                options.Services.AddSingleton<ICalendarMetrics, NullCalendarMetrics>();
                options.Services.AddSingleton(_gate);
                options.Services.AddScoped<AppointmentRepository>();
                options.Services.AddScoped<IAppointmentRepository>(sp => new HoldableAppointments(
                    sp.GetRequiredService<AppointmentRepository>(),
                    sp.GetRequiredService<HoldingGate>()
                ));
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CalendarDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.UseTaxVisionDurableLocalQueues();

                options.PublishMessage<AppointmentCancelledIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<ReminderTargetClosedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<AppointmentStartingSoonIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private async Task<bool> OutgoingReachesAsync(string messageType, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await CountRowsAsync("wolverine_outgoing_envelopes", messageType) == expected)
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

    private CalendarDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CalendarDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

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

    /// <summary>Retiene la lectura del handler de dominio, que es la que sigue a la del comando.</summary>
    private sealed class HoldingGate
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remainingReadsBeforeHold = -1;

        public void HoldNextHandlerRead() => _remainingReadsBeforeHold = 1;

        public async Task OnReadAsync()
        {
            if (_remainingReadsBeforeHold < 0)
                return;
            if (Interlocked.Decrement(ref _remainingReadsBeforeHold) != -1)
                return;

            _reached.TrySetResult();
            await _released.Task;
        }

        public async Task<bool> ReachedAsync() =>
            await Task.WhenAny(_reached.Task, Task.Delay(TimeSpan.FromSeconds(30))) == _reached.Task;

        public void Release() => _released.TrySetResult();
    }

    private sealed class HoldableAppointments(AppointmentRepository inner, HoldingGate gate) : IAppointmentRepository
    {
        public async Task<Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        )
        {
            await gate.OnReadAsync();
            return await inner.GetByIdAsync(tenantId, appointmentId, ct);
        }

        public Task<Result<Appointment>> GetByIdForReadAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => inner.GetByIdForReadAsync(tenantId, appointmentId, assignedToUserId, ct);

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => inner.ListForRangeAsync(tenantId, rangeStartUtc, rangeEndUtc, ct);

        public Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => inner.ListVisibleForRangeAsync(tenantId, rangeStartUtc, rangeEndUtc, assignedToUserId, ct);

        public Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => inner.ListForUserRangeAsync(tenantId, userId, rangeStartUtc, rangeEndUtc, ct);

        public Task<IReadOnlyList<Appointment>> ListFutureByOrganizerAsync(
            Guid tenantId,
            Guid organizerUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => inner.ListFutureByOrganizerAsync(tenantId, organizerUserId, nowUtc, ct);

        public Task<IReadOnlyList<Appointment>> ListFutureByStaffAttendeeAsync(
            Guid tenantId,
            Guid userId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => inner.ListFutureByStaffAttendeeAsync(tenantId, userId, nowUtc, ct);

        public Task<int> CountFutureByOrganizerAsync(
            Guid tenantId,
            Guid organizerUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => inner.CountFutureByOrganizerAsync(tenantId, organizerUserId, nowUtc, ct);

        public void Add(Appointment appointment) => inner.Add(appointment);

        public void Remove(Appointment appointment) => inner.Remove(appointment);

        public void DiscardPendingChanges() => inner.DiscardPendingChanges();
    }

    // Falla después de guardar la cancelación y antes de confirmar: la transacción de Wolverine deshace fila y sobre.
    private sealed class FailAfterCancelInterceptor : SaveChangesInterceptor
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
                    .Context!.ChangeTracker.Entries<Appointment>()
                    .Any(e => e.Entity.Status == AppointmentStatus.Cancelled);
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
