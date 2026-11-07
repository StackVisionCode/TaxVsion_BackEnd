using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Scheduling;
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
/// CAL-6 (D-CAL-2, regla dura): una franja bloqueante no se ocupa ni al crear ni al mover. Corre por el pipeline real
/// de Wolverine, porque el candado vive en la transacción que abre el middleware.
/// </summary>
public sealed class AppointmentConflictIntegrationTests : IAsyncLifetime
{
    private const string EnvelopeSchema = "calconflict";
    private const string NewYork = "America/New_York";
    private const string WeeklyRule = "FREQ=WEEKLY";
    private static readonly TimeOnly Ten = new(10, 0);
    private static readonly DateOnly FirstDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private readonly ProbeBarrier _barrier = new();
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
    public async Task TwoBlockingSchedulesSameSlot_InParallel_OnlyOneSucceeds()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var start = DateTime.UtcNow.AddDays(2);

        var results = await Task.WhenAll(
            ScheduleAsync(type, start, _organizer),
            ScheduleAsync(type, start, _organizer)
        );

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(AppointmentErrors.Conflict, Assert.Single(results, r => r.IsFailure).Error);
        Assert.Equal(1, await CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task TwoOrganizersSameSlot_DoNotWaitForEachOther()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var start = DateTime.UtcNow.AddDays(2);

        var results = await Task.WhenAll(
            ScheduleAsync(type, start, _organizer),
            ScheduleAsync(type, start, Guid.NewGuid())
        );

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.True(_barrier.BothArrived);
    }

    [Fact(Timeout = 120_000)]
    public async Task RescheduleOntoABlockingSlot_IsRefused_AndNothingChanges()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var taken = DateTime.UtcNow.AddDays(2);
        await ScheduleOkAsync(type, taken);
        var start = taken.AddDays(1);
        var moved = await ScheduleOkAsync(type, start);
        await InviteAndAcceptAsync(moved);

        var result = await RescheduleAsync(OneOff(moved, taken));

        Assert.Equal(AppointmentErrors.Conflict, result.Error);
        var stored = await ReadAsync(moved);
        Assert.Equal(start, stored.Timing.StartUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(AttendeeResponse.Accepted, Assert.Single(stored.Attendees).Response);
    }

    /// <summary>La agenda que cuenta es la del organizador, también cuando mueve la cita otra persona.</summary>
    [Fact(Timeout = 120_000)]
    public async Task RescheduleByAnotherUserWithManageAll_ChecksTheOrganizersAgenda()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var taken = DateTime.UtcNow.AddDays(2);
        await ScheduleOkAsync(type, taken);
        var moved = await ScheduleOkAsync(type, taken.AddDays(1));

        var result = await RescheduleAsync(
            OneOff(moved, taken) with
            {
                ActingUserId = Guid.NewGuid(),
                CanManageAll = true,
            }
        );

        Assert.Equal(AppointmentErrors.Conflict, result.Error);
    }

    /// <summary>Sin permiso sobre la cita, ese error va primero: el 409 contaría que el organizador tiene ocupada la franja.</summary>
    [Fact(Timeout = 120_000)]
    public async Task RescheduleByAStranger_GetsNotTheOrganizer_NotTheConflict()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var taken = DateTime.UtcNow.AddDays(2);
        await ScheduleOkAsync(type, taken);
        var moved = await ScheduleOkAsync(type, taken.AddDays(1));

        var result = await RescheduleAsync(OneOff(moved, taken) with { ActingUserId = Guid.NewGuid() });

        Assert.Equal(AppointmentErrors.NotTheOrganizer, result.Error);
    }

    [Fact(Timeout = 120_000)]
    public async Task RescheduleOntoANonBlockingSlot_StillMoves()
    {
        var type = await SeedTypeAsync(blocksOnConflict: false);
        var taken = DateTime.UtcNow.AddDays(2);
        await ScheduleOkAsync(type, taken);
        var moved = await ScheduleOkAsync(type, taken.AddDays(1));

        var result = await RescheduleAsync(OneOff(moved, taken));

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 120_000)]
    public async Task RescheduleIntoABlockedTime_IsRefused_EvenWithANonBlockingType()
    {
        var type = await SeedTypeAsync(blocksOnConflict: false);
        var start = DateTime.UtcNow.AddDays(2);
        var moved = await ScheduleOkAsync(type, start);
        var vacation = start.AddDays(1);
        await using (var db = _database.NewContext())
        {
            db.BlockedTimes.Add(
                BlockedTime.Create(_tenantId, _organizer, vacation, vacation.AddHours(8), null, DateTime.UtcNow).Value
            );
            await db.SaveChangesAsync();
        }

        var result = await RescheduleAsync(OneOff(moved, vacation.AddHours(1)));

        Assert.Equal(AppointmentErrors.Conflict, result.Error);
    }

    [Fact(Timeout = 120_000)]
    public async Task TwoMovesOntoTheSameFreeSlot_InParallel_OnlyOneSucceeds()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var first = await ScheduleOkAsync(type, DateTime.UtcNow.AddDays(2));
        var second = await ScheduleOkAsync(type, DateTime.UtcNow.AddDays(3));
        var free = DateTime.UtcNow.AddDays(4);

        var results = await Task.WhenAll(RescheduleAsync(OneOff(first, free)), RescheduleAsync(OneOff(second, free)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(AppointmentErrors.Conflict, Assert.Single(results, r => r.IsFailure).Error);
    }

    [Fact(Timeout = 120_000)]
    public async Task MovingAnOccurrenceOntoABlockingSlot_IsRefused_AndLeavesNoException()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);
        var taken = UtcAt(FirstDate.AddDays(9));
        await ScheduleOkAsync(type, taken);

        var result = await RescheduleAsync(MoveOccurrence(series, taken));

        Assert.Equal(AppointmentErrors.Conflict, result.Error);
        Assert.Empty((await ReadAsync(series)).Exceptions);
    }

    /// <summary>Solo cuenta la ocurrencia que se mueve: otra de la serie que ya chocaba no frena el cambio.</summary>
    [Fact(Timeout = 120_000)]
    public async Task MovingAnOccurrence_IgnoresAClashElsewhereInTheSeries()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);
        await SeedOverlapAsync(type, UtcAt(FirstDate.AddDays(14)));

        var result = await RescheduleAsync(MoveOccurrence(series, UtcAt(FirstDate.AddDays(9))));

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 120_000)]
    public async Task MovingTheEntireSeriesOntoABlockingSlot_IsRefused_AndNothingChanges()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);
        await ScheduleOkAsync(type, UtcAt(FirstDate.AddDays(2)));

        var result = await RescheduleAsync(MoveSeries(series, EditScope.EntireSeries, FirstDate.AddDays(2), Ten));

        Assert.Equal(AppointmentErrors.Conflict, result.Error);
        Assert.Equal(FirstDate, (await ReadAsync(series)).Timing.SeriesStartDate);
    }

    /// <summary>La serie conserva sus excepciones: una ocurrencia cancelada no ocupa la franja.</summary>
    [Fact(Timeout = 120_000)]
    public async Task MovingTheEntireSeries_IgnoresACancelledOccurrence()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);
        await ScheduleOkAsync(type, UtcAt(FirstDate.AddDays(7), Ten.AddHours(2)));
        await using (var db = _database.NewContext())
        {
            var stored = await db
                .Appointments.IgnoreQueryFilters()
                .Include(a => a.Exceptions)
                .SingleAsync(a => a.Id == series);
            Assert.True(
                stored.CancelOccurrence(UtcAt(FirstDate.AddDays(7)), _organizer, false, DateTime.UtcNow).IsSuccess
            );
            await db.SaveChangesAsync();
        }

        // Misma hora de inicio, más larga: la excepción sigue apuntando a esa ocurrencia.
        var result = await RescheduleAsync(
            MoveSeries(series, EditScope.EntireSeries, FirstDate, Ten) with
            {
                Duration = TimeSpan.FromHours(3),
            }
        );

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 120_000)]
    public async Task SplittingOntoABlockingSlot_IsRefused_AndTheSeriesStaysWhole()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);
        await ScheduleOkAsync(type, UtcAt(FirstDate.AddDays(9)));
        var rule = (await ReadAsync(series)).Recurrence!.Value;

        var result = await RescheduleAsync(
            MoveSeries(series, EditScope.ThisAndFollowing, FirstDate.AddDays(9), Ten) with
            {
                OriginalStartUtc = UtcAt(FirstDate.AddDays(7)),
            }
        );

        Assert.Equal(AppointmentErrors.Conflict, result.Error);
        Assert.Equal(rule, (await ReadAsync(series)).Recurrence!.Value);
        Assert.Equal(2, await CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task MovingAOneOffWithinItsOwnSlot_DoesNotClashWithItself()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var start = DateTime.UtcNow.AddDays(2);
        var moved = await ScheduleOkAsync(type, start);

        var result = await RescheduleAsync(OneOff(moved, start.AddMinutes(30)));

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 120_000)]
    public async Task MovingAnOccurrenceWithinItsOwnSlot_DoesNotClashWithItself()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);

        var result = await RescheduleAsync(MoveOccurrence(series, UtcAt(FirstDate.AddDays(7), Ten.AddMinutes(30))));

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 120_000)]
    public async Task MovingTheEntireSeriesWithinItsOwnSlots_DoesNotClashWithItself()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);

        var result = await RescheduleAsync(MoveSeries(series, EditScope.EntireSeries, FirstDate, Ten.AddMinutes(30)));

        Assert.True(result.IsSuccess);
    }

    /// <summary>La serie nueva tiene otro id, pero sigue siendo la misma cita: no choca con lo que queda de la vieja.</summary>
    [Fact(Timeout = 120_000)]
    public async Task SplittingOverTheOldHalfOfTheSeries_DoesNotClashWithItself()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var series = await ScheduleSeriesOkAsync(type);

        var result = await RescheduleAsync(
            MoveSeries(series, EditScope.ThisAndFollowing, FirstDate, Ten.AddMinutes(30)) with
            {
                OriginalStartUtc = UtcAt(FirstDate.AddDays(7)),
            }
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await CountAsync());
    }

    /// <summary>Un solape que ya existía no impide cambiar el aviso: la cita no se mueve.</summary>
    [Fact(Timeout = 120_000)]
    public async Task ChangingOnlyTheReminder_OnAnOldOverlap_StillWorks()
    {
        var type = await SeedTypeAsync(blocksOnConflict: true);
        var appointment = await ScheduleOkAsync(type, DateTime.UtcNow.AddDays(2));
        var start = (await ReadAsync(appointment)).Timing.StartUtc!.Value;
        await SeedOverlapAsync(type, start);

        var result = await RescheduleAsync(OneOff(appointment, start) with { ReminderLeadMinutes = 30 });

        Assert.True(result.IsSuccess);
    }

    private Task<Result<AppointmentWithWarnings>> ScheduleAsync(Guid typeId, DateTime startUtc, Guid organizer) =>
        _host!
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<AppointmentWithWarnings>>(
                new ScheduleAppointmentCommand(
                    _tenantId,
                    organizer,
                    "Signing",
                    null,
                    null,
                    typeId,
                    "America/New_York",
                    startUtc,
                    startUtc.AddHours(1),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false
                )
            );

    private Task<Result<AppointmentResponse>> RescheduleAsync(RescheduleAppointmentCommand command) =>
        _host!.Services.GetRequiredService<IMessageBus>().InvokeAsync<Result<AppointmentResponse>>(command);

    private RescheduleAppointmentCommand OneOff(Guid appointmentId, DateTime newStartUtc) =>
        new(
            _tenantId,
            appointmentId,
            _organizer,
            null,
            null,
            newStartUtc,
            newStartUtc.AddHours(1),
            null,
            null,
            null,
            null,
            null,
            false
        );

    /// <summary>La segunda ocurrencia de la serie, llevada a otra hora.</summary>
    private RescheduleAppointmentCommand MoveOccurrence(Guid seriesId, DateTime newStartUtc) =>
        OneOff(seriesId, newStartUtc) with
        {
            Scope = EditScope.ThisOccurrence,
            OriginalStartUtc = UtcAt(FirstDate.AddDays(7)),
        };

    private RescheduleAppointmentCommand MoveSeries(
        Guid seriesId,
        EditScope scope,
        DateOnly firstDate,
        TimeOnly time
    ) =>
        new(
            _tenantId,
            seriesId,
            _organizer,
            scope,
            null,
            null,
            null,
            firstDate,
            time,
            TimeSpan.FromHours(1),
            NewYork,
            WeeklyRule,
            false
        );

    private async Task<Guid> ScheduleOkAsync(Guid typeId, DateTime startUtc)
    {
        var scheduled = await ScheduleAsync(typeId, startUtc, _organizer);
        Assert.True(scheduled.IsSuccess);
        return scheduled.Value.Appointment.Id;
    }

    private async Task<Guid> ScheduleSeriesOkAsync(Guid typeId)
    {
        var scheduled = await _host!
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<AppointmentWithWarnings>>(
                new ScheduleAppointmentCommand(
                    _tenantId,
                    _organizer,
                    "Weekly signing",
                    null,
                    null,
                    typeId,
                    NewYork,
                    null,
                    null,
                    FirstDate,
                    Ten,
                    TimeSpan.FromHours(1),
                    WeeklyRule,
                    null,
                    null,
                    false
                )
            );
        Assert.True(scheduled.IsSuccess);
        return scheduled.Value.Appointment.Id;
    }

    /// <summary>Un solape de antes de la regla: entra directo a la base, sin pasar por el alta.</summary>
    private async Task SeedOverlapAsync(Guid typeId, DateTime startUtc)
    {
        await using var db = _database.NewContext();
        db.Appointments.Add(
            Appointment
                .Schedule(
                    _tenantId,
                    AppointmentTitle.Create("Old overlap").Value,
                    EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), NewYork).Value,
                    typeId,
                    _organizer,
                    DateTime.UtcNow
                )
                .Value
        );
        await db.SaveChangesAsync();
    }

    private async Task InviteAndAcceptAsync(Guid appointmentId)
    {
        var guest = Guid.NewGuid();
        await using var db = _database.NewContext();
        var appointment = await db
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .SingleAsync(a => a.Id == appointmentId);
        Assert.True(
            appointment
                .AddAttendee(
                    AttendeeKind.InternalUser,
                    guest,
                    null,
                    AttendeeSnapshot.Create("Guest", "guest@firm.test").Value,
                    false,
                    _organizer,
                    DateTime.UtcNow,
                    false
                )
                .IsSuccess
        );
        Assert.True(
            appointment.RespondAsAttendee(AttendeeResponse.Accepted, guest, null, null, DateTime.UtcNow).IsSuccess
        );
        await db.SaveChangesAsync();
    }

    private async Task<Appointment> ReadAsync(Guid id)
    {
        await using var db = _database.NewContext();
        return await db
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .SingleAsync(a => a.Id == id);
    }

    private static DateTime UtcAt(DateOnly date, TimeOnly? time = null) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time ?? Ten), TimeZoneInfo.FindSystemTimeZoneById(NewYork));

    private async Task<Guid> SeedTypeAsync(bool blocksOnConflict)
    {
        var type = AppointmentType
            .Create(
                _tenantId,
                "In-person signing",
                TimeSpan.FromHours(1),
                "#336699",
                DateTime.UtcNow,
                blocksOnConflict: blocksOnConflict
            )
            .Value;
        await using var db = _database.NewContext();
        db.AppointmentTypes.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    private async Task<int> CountAsync()
    {
        await using var db = _database.NewContext();
        return await db.Appointments.IgnoreQueryFilters().CountAsync(a => a.TenantId == _tenantId);
    }

    private async Task<IHost> StartHostAsync() =>
        await Host.CreateDefaultBuilder()
            // El temporizador de Wolverine puede registrar tras el Dispose: EventLog lanzaría y tumbaría el proceso.
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ServiceName = "cal-6-conflict-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ScheduleAppointmentHandler))
                    .IncludeType(typeof(RescheduleAppointmentHandler));
                options.PersistMessagesWithSqlServer(_database.ConnectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                options.Services.AddDbContext<CalendarDbContext>(db => db.UseSqlServer(_database.ConnectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
                options.Services.AddSingleton<ITenantContext, CalendarTemporaryDatabase.NoTenantContext>();
                options.Services.AddSingleton<ICalendarMetrics, NullCalendarMetrics>();
                options.Services.AddSingleton(_barrier);
                options.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
                options.Services.AddOptions<OrganizerScheduleLockOptions>();
                options.Services.AddScoped<IOrganizerScheduleLock, SqlOrganizerScheduleLock>();
                options.Services.AddScoped<IAppointmentTypeRepository, AppointmentTypeRepository>();
                options.Services.AddScoped<AvailabilityRepository>();
                options.Services.AddScoped<IAvailabilityRepository>(sp => new BarrierAvailability(
                    sp.GetRequiredService<AvailabilityRepository>(),
                    sp.GetRequiredService<ProbeBarrier>()
                ));
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CalendarDbContext>();
                options.Policies.AutoApplyTransactions();
            })
            .StartAsync();

    /// <summary>
    /// Espera al otro lado justo después de la lectura del probe. Con plazo: bien hecho, el segundo ni llega aquí
    /// hasta que el primero confirma, y sin plazo la prueba se quedaría esperándolo.
    /// </summary>
    private sealed class ProbeBarrier
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public bool BothArrived => _both.Task.IsCompleted;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == 2)
                _both.TrySetResult();
            await Task.WhenAny(_both.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        }
    }

    private sealed class BarrierAvailability(IAvailabilityRepository inner, ProbeBarrier barrier)
        : IAvailabilityRepository
    {
        public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => inner.ListRulesAsync(tenantId, userId, ct);

        public async Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default
        )
        {
            var blocks = await inner.ListBlocksAsync(tenantId, userId, fromUtc, toUtc, ct);
            await barrier.ArriveAsync();
            return blocks;
        }

        public Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => inner.ListAllBlocksForUserAsync(tenantId, userId, ct);

        public void AddRule(AvailabilityRule rule) => inner.AddRule(rule);

        public void AddBlock(BlockedTime block) => inner.AddBlock(block);

        public void RemoveBlock(BlockedTime block) => inner.RemoveBlock(block);
    }
}
