using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Api.Controllers;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.Queries;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// RSVP contra RSVP no es un choque de verdad; RSVP contra reprogramar sí. Corre por el endpoint real: quien responde
/// no debe ver un 409 por otro asistente, y su respuesta nunca se aplica en silencio a una hora que no aceptó.
/// </summary>
public sealed class AppointmentResponseRaceIntegrationTests : IAsyncLifetime
{
    private const string EnvelopeSchema = "calrsvp";
    private const string ConcurrencyConflict = "Persistence.ConcurrencyConflict";

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private readonly Guid _firstGuest = Guid.NewGuid();
    private readonly Guid _secondGuest = Guid.NewGuid();
    private readonly SaveRace _race = new();
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

    [Fact(Timeout = 120_000)]
    public async Task TwoAttendeesResponding_AtOnce_BothAreSaved()
    {
        var id = await SeedAsync();
        _race.WaitForBoth();

        var results = await Task.WhenAll(
            RespondAsync(id, _firstGuest, AttendeeResponse.Accepted),
            RespondAsync(id, _secondGuest, AttendeeResponse.Declined)
        );

        Assert.All(results, result => Assert.IsType<NoContentResult>(result));
        Assert.True(_race.BothArrived);
        var stored = await ReadAsync(id);
        Assert.Equal(AttendeeResponse.Accepted, ResponseOf(stored, _firstGuest));
        Assert.Equal(AttendeeResponse.Declined, ResponseOf(stored, _secondGuest));
    }

    /// <summary>La reprogramación pone las respuestas a cero: el reintento no puede aceptar la hora nueva por él.</summary>
    [Fact(Timeout = 120_000)]
    public async Task ResponseThatLosesToAReschedule_IsNotCarriedOverToTheNewTime()
    {
        var id = await SeedAsync();
        var newStart = DateTime.UtcNow.AddDays(5);
        _race.RunRivalOnFirstSave(() =>
            RivalAsync(
                id,
                a =>
                    a.Reschedule(
                        EventTiming.PointInTimeOf(newStart, newStart.AddHours(1), "America/New_York").Value,
                        _organizer,
                        false,
                        DateTime.UtcNow
                    )
            )
        );

        var result = await RespondAsync(id, _firstGuest, AttendeeResponse.Accepted);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(ConcurrencyConflict, Assert.IsType<Error>(conflict.Value).Code);
        var stored = await ReadAsync(id);
        Assert.Equal(AttendeeResponse.NeedsAction, ResponseOf(stored, _firstGuest));
        Assert.NotNull(stored.Timing.StartUtc);
        Assert.Equal(newStart, stored.Timing.StartUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact(Timeout = 120_000)]
    public async Task ResponseThatLosesToACancellation_EndsAsCancelled()
    {
        var id = await SeedAsync();
        _race.RunRivalOnFirstSave(() => RivalAsync(id, a => a.Cancel(_organizer, false, null, DateTime.UtcNow)));

        var result = await RespondAsync(id, _firstGuest, AttendeeResponse.Accepted);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(AppointmentErrors.CancelledIsFinal, Assert.IsType<Error>(refused.Value));
        Assert.Equal(AttendeeResponse.NeedsAction, ResponseOf(await ReadAsync(id), _firstGuest));
    }

    /// <summary>Al revés sigue igual: quien reprograma sin ver una respuesta recién guardada recibe el conflicto.</summary>
    [Fact(Timeout = 120_000)]
    public async Task RescheduleThatLosesToAResponse_StillGetsTheConflict()
    {
        var id = await SeedAsync();
        var newStart = DateTime.UtcNow.AddDays(5);
        _race.RunRivalOnFirstSave(() =>
            RivalAsync(
                id,
                a => a.RespondAsAttendee(AttendeeResponse.Accepted, _firstGuest, null, null, DateTime.UtcNow)
            )
        );

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            _host!
                .Services.GetRequiredService<IMessageBus>()
                .InvokeAsync<Result<AppointmentResponse>>(
                    new RescheduleAppointmentCommand(
                        _tenantId,
                        id,
                        _organizer,
                        null,
                        null,
                        newStart,
                        newStart.AddHours(1),
                        null,
                        null,
                        null,
                        null,
                        null,
                        false
                    )
                )
        );

        Assert.Equal(ConcurrencyConflict, conflict.Code);
        Assert.Equal(AttendeeResponse.Accepted, ResponseOf(await ReadAsync(id), _firstGuest));
    }

    private async Task<IActionResult> RespondAsync(Guid appointmentId, Guid userId, AttendeeResponse response)
    {
        using var scope = _host!.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<AppointmentsController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity([
                        new Claim(ClaimNames.TenantId, _tenantId.ToString()),
                        new Claim("sub", userId.ToString()),
                    ])
                ),
            },
        };
        return await controller.Respond(
            appointmentId,
            new RespondToAppointmentRequest(response),
            CancellationToken.None
        );
    }

    private async Task RivalAsync(Guid appointmentId, Func<Appointment, Result> change)
    {
        await using var db = _database.NewContext();
        var appointment = await LoadAsync(db, appointmentId);
        Assert.True(change(appointment).IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAsync()
    {
        var start = DateTime.UtcNow.AddDays(2);
        var appointment = Appointment
            .Schedule(
                _tenantId,
                AppointmentTitle.Create("Client review").Value,
                EventTiming.PointInTimeOf(start, start.AddHours(1), "America/New_York").Value,
                Guid.NewGuid(),
                _organizer,
                DateTime.UtcNow
            )
            .Value;
        Invite(appointment, _firstGuest, "first@firm.test");
        Invite(appointment, _secondGuest, "second@firm.test");

        await using var db = _database.NewContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private void Invite(Appointment appointment, Guid userId, string email) =>
        Assert.True(
            appointment
                .AddAttendee(
                    AttendeeKind.InternalUser,
                    userId,
                    null,
                    AttendeeSnapshot.Create("Guest", email).Value,
                    false,
                    _organizer,
                    DateTime.UtcNow,
                    false
                )
                .IsSuccess
        );

    private async Task<Appointment> ReadAsync(Guid id)
    {
        await using var db = _database.NewContext();
        return await LoadAsync(db, id);
    }

    private static Task<Appointment> LoadAsync(CalendarDbContext db, Guid id) =>
        db
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .SingleAsync(a => a.Id == id);

    private static AttendeeResponse ResponseOf(Appointment appointment, Guid userId) =>
        appointment.Attendees.Single(a => a.UserId == userId).Response;

    private async Task<IHost> StartHostAsync() =>
        await Host.CreateDefaultBuilder()
            // El temporizador de Wolverine puede registrar tras el Dispose: EventLog lanzaría y tumbaría el proceso.
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ServiceName = "cal-rsvp-race-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(RespondToAppointmentHandler))
                    .IncludeType(typeof(GetAppointmentScheduleStampHandler))
                    .IncludeType(typeof(RescheduleAppointmentHandler));
                options.PersistMessagesWithSqlServer(_database.ConnectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                options.Services.AddDbContext<CalendarDbContext>(db =>
                    db.UseSqlServer(_database.ConnectionString).AddInterceptors(_race)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
                options.Services.AddSingleton<ITenantContext, CalendarTemporaryDatabase.NoTenantContext>();
                options.Services.AddSingleton<ICalendarMetrics, NullCalendarMetrics>();
                options.Services.AddSingleton<IUserPermissionsSource, NoPermissions>();
                options.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
                options.Services.AddOptions<OrganizerScheduleLockOptions>();
                options.Services.AddScoped<IOrganizerScheduleLock, SqlOrganizerScheduleLock>();
                options.Services.AddScoped<IAppointmentTypeRepository>(_ => new FixedTypeRepository());
                options.Services.AddScoped<IAvailabilityRepository>(_ => new EmptyAvailability());
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CalendarDbContext>();
                options.Policies.AutoApplyTransactions();
                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

    private sealed class NoPermissions : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    /// <summary>Las citas de estas pruebas no siembran su tipo: aquí no se comprueba el choque de agenda.</summary>
    private sealed class FixedTypeRepository : IAppointmentTypeRepository
    {
        public Task<Result<AppointmentType>> GetByIdAsync(Guid tenantId, Guid typeId, CancellationToken ct = default) =>
            Task.FromResult(
                Result.Success(
                    AppointmentType.Create(tenantId, "Review", TimeSpan.FromHours(1), "#336699", DateTime.UtcNow).Value
                )
            );

        public Task<IReadOnlyList<AppointmentType>> ListAsync(
            Guid tenantId,
            bool onlyActive,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<AppointmentType>>([]);

        public Task<bool> AnyAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(true);

        public void Add(AppointmentType type) { }
    }

    /// <summary>
    /// Se mete justo antes de guardar, con el agregado ya leído: los dos primeros se esperan, o una escritura rival
    /// entra antes del primero. Los guardados siguientes son reintentos y pasan de largo.
    /// </summary>
    private sealed class SaveRace : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _waitForBoth;
        private Func<Task>? _rival;
        private int _arrived;

        public bool BothArrived => _both.Task.IsCompleted;

        public void WaitForBoth() => _waitForBoth = true;

        public void RunRivalOnFirstSave(Func<Task> rival) => _rival = rival;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var arrival = Interlocked.Increment(ref _arrived);
            if (_rival is { } rival && arrival == 1)
                await rival();

            if (_waitForBoth)
            {
                if (arrival == 2)
                    _both.TrySetResult();
                if (arrival <= 2)
                    await Task.WhenAny(_both.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            }

            return result;
        }
    }
}
