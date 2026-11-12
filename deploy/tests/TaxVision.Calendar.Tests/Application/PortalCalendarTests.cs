using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Portal;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// El calendario del cliente en el portal (B6) y su reserva (B7). El cliente solo ve lo suyo, reserva
/// con su preparador asignado en los huecos reales, y el servidor decide si puede cancelar o mover.
/// </summary>
public sealed class PortalCalendarTests
{
    private const string NewYork = "America/New_York";
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();
    private readonly Guid _portalUser = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();
    private readonly AppointmentType _type;
    private readonly InMemoryAppointments _appointments = new();
    private readonly Store _store;
    private readonly Policies _policies = new();
    private readonly Rules _rules;
    private readonly InMemoryDirectory _directory = new();

    public PortalCalendarTests()
    {
        _type = AppointmentType
            .Create(_tenant, "Tax review", TimeSpan.FromMinutes(30), "#336699", DateTime.UtcNow)
            .Value;
        _store = new Store(_appointments, _customer, [_olga]);
        _rules = new Rules(
            AvailabilityRule
                .Create(
                    _tenant,
                    _olga,
                    DaysOfWeekMask.Weekdays,
                    new TimeOnly(9, 0),
                    new TimeOnly(17, 0),
                    NewYork,
                    DateTime.UtcNow
                )
                .Value
        );
    }

    /// <summary>Un lunes de dentro de dos o tres semanas, a la hora local dada.</summary>
    private static DateTime Monday(int hour, int minute = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(NewYork);
        var day = DateTime.UtcNow.Date.AddDays(14);
        while (day.DayOfWeek != DayOfWeek.Monday)
            day = day.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified),
            zone
        );
    }

    private void EnableBooking(int cutoffMinutes = 1440)
    {
        var policy = BookingPolicy.Default(_tenant);
        policy.Update(true, [_type.Id], 1440, 60, 0, 30, cutoffMinutes, DateTime.UtcNow, requiresApproval: false);
        _policies.Policy = policy;
    }

    private Appointment Booked(Guid? customerId, DateTime start, Guid? attendeeCustomer = null)
    {
        attendeeCustomer ??= customerId;
        var appointment = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Tax review").Value,
                EventTiming.PointInTimeOf(start, start.AddMinutes(30), NewYork).Value,
                _type.Id,
                _olga,
                DateTime.UtcNow,
                customerId: customerId
            )
            .Value;
        if (attendeeCustomer is { } guest)
            appointment.AddAttendee(
                AttendeeKind.Customer,
                null,
                guest,
                AttendeeSnapshot.Create("Guest client", "guest@client.test").Value,
                isRequired: true,
                _olga,
                DateTime.UtcNow,
                canManageAll: false
            );
        appointment.ClearDomainEvents();
        _appointments.All.Add(appointment);
        return appointment;
    }

    private Task<Result<PortalAppointmentResponse>> Book(
        string key,
        DateTime start,
        string? tokenName = "Ana Client"
    ) =>
        BookAsCustomerHandler.Handle(
            new BookAsCustomerCommand(
                _tenant,
                _customer,
                _portalUser,
                tokenName,
                "ana@client.test",
                key,
                _type.Id,
                _olga,
                start
            ),
            _store,
            _directory,
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new NoOffice(),
            _policies,
            new Contacts(_olga),
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

    [Fact]
    public async Task The_client_sees_only_their_appointments_and_never_the_cancelled_ones()
    {
        var mine = Booked(_customer, Monday(10));
        var asGuest = Booked(null, Monday(11), attendeeCustomer: _customer);
        Booked(_otherCustomer, Monday(12));
        var cancelled = Booked(_customer, Monday(13));
        cancelled.Cancel(_olga, false, null, DateTime.UtcNow);

        var result = await ListPortalAppointmentsHandler.Handle(
            new ListPortalAppointmentsQuery(_tenant, _customer, Monday(0), Monday(23)),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            new Contacts(_olga),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { mine.Id, asGuest.Id }.Order(), result.Value.Select(a => a.Id).Order());
        Assert.All(result.Value, a => Assert.Equal("Olga Preparer", a.PreparerDisplayName));
        Assert.All(result.Value, a => Assert.Equal("Tax review", a.TypeName));
    }

    [Fact]
    public async Task A_range_longer_than_62_days_or_someone_elses_appointment_is_refused()
    {
        var theirs = Booked(_otherCustomer, Monday(10));

        var tooLong = await ListPortalAppointmentsHandler.Handle(
            new ListPortalAppointmentsQuery(_tenant, _customer, Monday(0), Monday(0).AddDays(63)),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            new Contacts(_olga),
            CancellationToken.None
        );
        var other = await GetPortalAppointmentHandler.Handle(
            new GetPortalAppointmentQuery(_tenant, _customer, theirs.Id),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            new Contacts(_olga),
            CancellationToken.None
        );

        Assert.True(tooLong.IsFailure);
        Assert.Equal(AppointmentErrors.NotFound, other.Error);
    }

    [Fact]
    public async Task The_server_says_when_the_client_can_cancel_or_move()
    {
        EnableBooking(cutoffMinutes: 1440);
        var later = Booked(_customer, Monday(10));
        var tomorrow = Booked(_customer, DateTime.UtcNow.AddHours(5));

        var farAway = await GetPortalAppointmentHandler.Handle(
            new GetPortalAppointmentQuery(_tenant, _customer, later.Id),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            new Contacts(_olga),
            default
        );
        var tooClose = await GetPortalAppointmentHandler.Handle(
            new GetPortalAppointmentQuery(_tenant, _customer, tomorrow.Id),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            new Contacts(_olga),
            default
        );

        Assert.True(farAway.Value.CanCancel && farAway.Value.CanReschedule);
        Assert.False(tooClose.Value.CanCancel || tooClose.Value.CanReschedule);
    }

    [Fact]
    public async Task The_client_answers_their_invitation_from_the_portal()
    {
        var invited = Booked(null, Monday(11), attendeeCustomer: _customer);

        var result = await RespondAsCustomerHandler.Handle(
            new RespondAsCustomerCommand(_tenant, _customer, invited.Id, AttendeeResponse.Accepted),
            _store,
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.Accepted, invited.Attendees.Single(a => a.CustomerId == _customer).Response);
    }

    [Fact]
    public async Task Options_say_why_booking_is_not_offered_and_list_the_assigned_preparers()
    {
        var off = await GetBookingOptionsHandler.Handle(
            new GetBookingOptionsQuery(_tenant, _customer),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            _rules,
            new NoOffice(),
            new Contacts(_olga),
            default
        );
        EnableBooking();
        var on = await GetBookingOptionsHandler.Handle(
            new GetBookingOptionsQuery(_tenant, _customer),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            _rules,
            new NoOffice(),
            new Contacts(_olga),
            default
        );
        var unassigned = await GetBookingOptionsHandler.Handle(
            new GetBookingOptionsQuery(_tenant, _otherCustomer),
            new Store(_appointments, _otherCustomer, []),
            new SingleTypeRepository(_type),
            _policies,
            _rules,
            new NoOffice(),
            new Contacts(_olga),
            default
        );

        Assert.False(off.Value.Enabled);
        Assert.Equal("NotEnabled", off.Value.Reason);
        Assert.True(on.Value.Enabled);
        Assert.Equal("Olga Preparer", Assert.Single(on.Value.Preparers).DisplayName);
        Assert.Equal(30, Assert.Single(on.Value.Types).DurationMinutes);
        Assert.Equal(NewYork, on.Value.TimeZoneId);
        Assert.Equal("NoPreparer", unassigned.Value.Reason);
    }

    [Fact]
    public async Task Slots_follow_the_preparers_hours_the_step_and_skip_whats_taken()
    {
        EnableBooking();
        Booked(_customer, Monday(10));

        var result = await GetBookingSlotsHandler.Handle(
            new GetBookingSlotsQuery(_tenant, _customer, _type.Id, _olga, Monday(0), Monday(23)),
            _store,
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new NoOffice(),
            _policies,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var starts = result.Value.Slots.Select(s => s.StartUtc).ToList();
        Assert.Equal(Monday(9), starts[0]);
        Assert.Equal(Monday(16, 30), starts[^1]);
        Assert.DoesNotContain(Monday(10), starts);
        Assert.Contains(Monday(10, 30), starts);
        Assert.All(result.Value.Slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndUtc - s.StartUtc));
    }

    [Fact]
    public async Task Booking_creates_the_appointment_once_per_key_and_a_taken_slot_is_refused()
    {
        EnableBooking();

        var first = await Book("key-1", Monday(10));
        var retry = await Book("key-1", Monday(10));
        var taken = await Book("key-2", Monday(10));
        var outOfHours = await Book("key-3", Monday(19));

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        var appointment = Assert.Single(_appointments.All);
        Assert.Equal(_olga, appointment.OrganizerUserId);
        Assert.Equal(_customer, appointment.CustomerId);
        Assert.Equal("Booked online by Ana Client.", appointment.Description);
        // Quien reserva ya dijo que va: nada de pedirle que acepte su propia cita.
        Assert.Contains(
            appointment.Attendees,
            a =>
                a.CustomerId == _customer
                && a.Snapshot.Email == "ana@client.test"
                && a.Response == AttendeeResponse.Accepted
        );
        // El aviso del preparador dice quién la reservó en línea.
        var scheduled = appointment
            .DomainEvents.OfType<TaxVision.Calendar.Domain.Appointments.Events.AppointmentScheduledDomainEvent>()
            .Single();
        Assert.Equal("Ana Client", scheduled.BookedByCustomerName);
        var outgoing =
            await TaxVision.Calendar.Application.Appointments.DomainEvents.AppointmentScheduledDomainEventHandler.Handle(
                scheduled,
                _appointments,
                new Wolverine.Envelope(),
                default
            );
        Assert.Equal(
            "Ana Client",
            outgoing
                .OfType<BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentScheduledIntegrationEvent>()
                .Single()
                .BookedByCustomerName
        );
        Assert.Equal(_type.Id, first.Value.TypeId);
        Assert.Equal(_olga, first.Value.PreparerUserId);
        Assert.Contains(appointment.Attendees, a => a.UserId == _olga && a.Response == AttendeeResponse.Accepted);
        Assert.Equal("Calendar.SlotTaken", taken.Error.Code);
        Assert.Equal("Calendar.SlotTaken", outOfHours.Error.Code);
    }

    /// <summary>
    /// El token del portal no trae el nombre: el del cliente sale del directorio de Calendar, no su correo
    /// (antes quedaba el correo como nombre y "Booked online by the client").
    /// </summary>
    [Fact]
    public async Task The_clients_name_comes_from_the_directory_not_their_email()
    {
        EnableBooking();
        _directory.Entries.Add(
            TaxVision.Calendar.Domain.Projections.CustomerDirectoryEntry.Create(
                _tenant,
                _customer,
                "Minato F Kenyimura",
                TaxVision.Calendar.Domain.Projections.CustomerDirectoryStatus.Active,
                DateTime.UtcNow
            )
        );

        var booked = await Book("key-1", Monday(10), tokenName: null);

        var appointment = Assert.Single(_appointments.All);
        Assert.True(booked.IsSuccess);
        Assert.Equal(
            "Minato F Kenyimura",
            appointment.Attendees.Single(a => a.CustomerId == _customer).Snapshot.DisplayName
        );
        Assert.Equal("Booked online by Minato F Kenyimura.", appointment.Description);
    }

    [Fact]
    public async Task Booking_with_someone_not_assigned_or_when_it_is_off_is_refused()
    {
        var off = await Book("key-1", Monday(10));
        EnableBooking();
        var stranger = await BookAsCustomerHandler.Handle(
            new BookAsCustomerCommand(
                _tenant,
                _customer,
                _portalUser,
                "Ana",
                "ana@client.test",
                "k",
                _type.Id,
                Guid.NewGuid(),
                Monday(10)
            ),
            _store,
            _directory,
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new NoOffice(),
            _policies,
            new Contacts(_olga),
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            default
        );

        Assert.Equal("Calendar.BookingNotAvailable", off.Error.Code);
        Assert.Equal("Calendar.BookingNotAvailable", stranger.Error.Code);
    }

    [Fact]
    public async Task The_client_cancels_or_moves_only_outside_the_cutoff()
    {
        EnableBooking();
        var later = Booked(_customer, Monday(10));
        var soon = Booked(_customer, DateTime.UtcNow.AddHours(5));

        var cancelSoon = await CancelAsCustomerHandler.Handle(
            new CancelAsCustomerCommand(_tenant, _customer, _portalUser, soon.Id, null),
            _store,
            _policies,
            new CountingUnitOfWork(),
            default
        );
        var moved = await RescheduleAsCustomerHandler.Handle(
            new RescheduleAsCustomerCommand(_tenant, _customer, _portalUser, later.Id, Monday(14)),
            _store,
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new NoOffice(),
            _policies,
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            default
        );
        var cancelLater = await CancelAsCustomerHandler.Handle(
            new CancelAsCustomerCommand(_tenant, _customer, _portalUser, later.Id, "Can't make it"),
            _store,
            _policies,
            new CountingUnitOfWork(),
            default
        );

        Assert.Equal("Calendar.ChangeTooLate", cancelSoon.Error.Code);
        Assert.True(moved.IsSuccess);
        Assert.Equal(Monday(14), later.Timing.StartUtc);
        Assert.Equal("Accepted", moved.Value.MyResponse);
        Assert.True(cancelLater.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, later.Status);
    }

    private sealed class Store(InMemoryAppointments appointments, Guid customer, Guid[] assigned) : IPortalCalendarStore
    {
        private readonly Dictionary<string, Guid> _bookings = [];

        public Task<IReadOnlyList<Appointment>> ListForCustomerAsync(
            Guid tenantId,
            Guid customerId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<Appointment>>([
                .. appointments.All.Where(a =>
                    a.TenantId == tenantId
                    && IsTheirs(a, customerId)
                    && a.Timing.StartUtc < toUtc
                    && a.Timing.EndUtc > fromUtc
                ),
            ]);

        public Task<Appointment?> FindForCustomerAsync(
            Guid tenantId,
            Guid customerId,
            Guid appointmentId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                appointments.All.FirstOrDefault(a =>
                    a.TenantId == tenantId && a.Id == appointmentId && IsTheirs(a, customerId)
                )
            );

        public Task<IReadOnlyList<Guid>> ListAssignedStaffAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>(customerId == customer ? assigned : []);

        public Task<IReadOnlyList<Guid>> ListOfficeStaffAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Guid?> FindBookingAsync(
            Guid tenantId,
            Guid customerId,
            string idempotencyKey,
            CancellationToken ct = default
        ) => Task.FromResult<Guid?>(_bookings.TryGetValue(idempotencyKey, out var id) ? id : null);

        public void AddBooking(BookingRequest booking) => _bookings[booking.IdempotencyKey] = booking.AppointmentId;

        private static bool IsTheirs(Appointment appointment, Guid customerId) =>
            appointment.CustomerId == customerId || appointment.Attendees.Any(a => a.CustomerId == customerId);
    }

    private sealed class Policies : IBookingPolicyRepository
    {
        public BookingPolicy? Policy { get; set; }

        public Task<BookingPolicy?> GetAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Policy);

        public void Add(BookingPolicy policy) => Policy = policy;
    }

    private sealed class Contacts(Guid olga) : IStaffContactClient
    {
        public Task<StaffContact?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(userId == olga ? new StaffContact("olga@office.com", "Olga Preparer", true) : null);
    }

    private sealed class NoOffice : IOfficeScheduleRepository
    {
        public Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<OfficeScheduleProjection?>(null);

        public void Add(OfficeScheduleProjection projection) { }
    }

    private sealed class Rules(params AvailabilityRule[] rules) : IAvailabilityRepository
    {
        public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<AvailabilityRule>>([.. rules.Where(r => r.UserId == userId)]);

        public Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<BlockedTime>>([]);

        public Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<BlockedTime>>([]);

        public Task<BlockedTime?> FindBlockAsync(Guid tenantId, Guid blockId, CancellationToken ct = default) =>
            Task.FromResult<BlockedTime?>(null);

        public void AddRule(AvailabilityRule rule) { }

        public void AddBlock(BlockedTime block) { }

        public void RemoveBlock(BlockedTime block) { }
    }
}
