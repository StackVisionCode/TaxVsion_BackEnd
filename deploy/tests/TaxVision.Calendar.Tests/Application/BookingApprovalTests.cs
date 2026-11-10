using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Application.Portal;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Types;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Con "Office approves each booking" (por defecto), la reserva del portal es una solicitud: aparta el
/// horario, la oficina la aprueba (para sí o para otro empleado) o la rechaza, y recién al aprobar se
/// anuncia como cita. El cliente puede pedir "Any available".
/// </summary>
public sealed class BookingApprovalTests
{
    private const string NewYork = "America/New_York";
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();
    private readonly Guid _andres = Guid.NewGuid();
    private readonly AppointmentType _type;
    private readonly InMemoryAppointments _appointments = new();
    private readonly InMemoryDirectory _directory = new();
    private readonly Policies _policies = new();
    private readonly Store _store;
    private readonly Rules _rules;

    public BookingApprovalTests()
    {
        _type = AppointmentType
            .Create(_tenant, "Consulta inicial", TimeSpan.FromMinutes(30), "#336699", DateTime.UtcNow)
            .Value;
        _store = new Store(_appointments, [_olga, _andres]);
        _rules = new Rules(
            AvailabilityRule
                .Create(
                    _tenant,
                    _olga,
                    DaysOfWeekMask.Weekdays,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0),
                    NewYork,
                    DateTime.UtcNow
                )
                .Value,
            AvailabilityRule
                .Create(
                    _tenant,
                    _andres,
                    DaysOfWeekMask.Weekdays,
                    new TimeOnly(13, 0),
                    new TimeOnly(17, 0),
                    NewYork,
                    DateTime.UtcNow
                )
                .Value
        );
        var policy = BookingPolicy.Default(_tenant);
        // Sin pasar requiresApproval: por defecto la oficina aprueba cada reserva.
        policy.Update(true, [_type.Id], 1440, 60, 0, 30, 1440, DateTime.UtcNow);
        _policies.Policy = policy;
    }

    private static DateTime Monday(int hour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(NewYork);
        var day = DateTime.UtcNow.Date.AddDays(14);
        while (day.DayOfWeek != DayOfWeek.Monday)
            day = day.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Unspecified),
            zone
        );
    }

    private Task<BuildingBlocks.Results.Result<PortalAppointmentResponse>> Book(
        Guid? preparer,
        DateTime start,
        string key = "k1"
    ) =>
        BookAsCustomerHandler.Handle(
            new BookAsCustomerCommand(
                _tenant,
                _customer,
                Guid.NewGuid(),
                "Minato",
                "minato@client.test",
                key,
                _type.Id,
                preparer,
                start
            ),
            _store,
            _directory,
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new NoOffice(),
            _policies,
            new Contacts(_olga, _andres),
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            default
        );

    [Fact]
    public async Task By_default_the_booking_is_a_request_that_holds_the_time_and_tells_the_office()
    {
        Assert.True(_policies.Policy!.RequiresApproval);

        var booked = await Book(_olga, Monday(10));

        Assert.True(booked.IsSuccess);
        Assert.Equal("Requested", booked.Value.Status);
        Assert.True(booked.Value.CanCancel);
        Assert.False(booked.Value.CanReschedule);
        var appointment = Assert.Single(_appointments.All);
        Assert.Equal(AppointmentStatus.Requested, appointment.Status);

        var requested = appointment.DomainEvents.OfType<AppointmentBookingRequestedDomainEvent>().Single();
        var outgoing = await AppointmentBookingRequestedDomainEventHandler.Handle(
            requested,
            _appointments,
            new Envelope(),
            default
        );
        Assert.Equal("Minato", outgoing!.CustomerName);
        Assert.Equal(_olga, outgoing.OrganizerUserId);

        // El horario queda apartado: otro cliente no puede pedirlo.
        Assert.Equal("Calendar.SlotTaken", (await Book(_olga, Monday(10), "k2")).Error.Code);
    }

    [Fact]
    public async Task Any_available_offers_the_union_of_the_preparers_and_picks_whoever_is_free()
    {
        var slots = await GetBookingSlotsHandler.Handle(
            new GetBookingSlotsQuery(_tenant, _customer, _type.Id, null, Monday(0), Monday(23)),
            _store,
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new NoOffice(),
            _policies,
            default
        );
        var options = await GetBookingOptionsHandler.Handle(
            new GetBookingOptionsQuery(_tenant, _customer),
            _store,
            new SingleTypeRepository(_type),
            _policies,
            _rules,
            new NoOffice(),
            new Contacts(_olga, _andres),
            default
        );

        Assert.True(options.Value.RequiresApproval);
        Assert.Contains(slots.Value.Slots, s => s.StartUtc == Monday(9));
        Assert.Contains(slots.Value.Slots, s => s.StartUtc == Monday(14));

        var afternoon = await Book(null, Monday(14));
        Assert.True(afternoon.IsSuccess);
        Assert.Equal(_andres, Assert.Single(_appointments.All).OrganizerUserId);
    }

    [Fact]
    public async Task The_office_approves_handing_it_to_another_employee_and_only_then_it_is_scheduled()
    {
        await Book(_olga, Monday(10));
        var appointment = Assert.Single(_appointments.All);
        appointment.ClearDomainEvents();

        var approved = await ApproveBookingRequestHandler.Handle(
            new ApproveBookingRequestCommand(_tenant, appointment.Id, _olga, _andres, false),
            _appointments,
            new SingleTypeRepository(_type),
            _rules,
            new Contacts(_olga, _andres),
            new CountingUnitOfWork(),
            default
        );

        Assert.True(approved.IsSuccess);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(_andres, appointment.OrganizerUserId);
        Assert.Contains(
            appointment.Attendees,
            a => a.UserId == _andres && a.Snapshot.DisplayName == "Andres Hernandez"
        );
        var scheduled = Assert.IsType<AppointmentScheduledDomainEvent>(Assert.Single(appointment.DomainEvents));
        // Viene de una solicitud: el portal del cliente se entera en vivo de que ya está confirmada.
        var outgoing = await AppointmentScheduledDomainEventHandler.Handle(
            scheduled,
            _appointments,
            new Envelope(),
            default
        );
        Assert.True(outgoing.OfType<AppointmentScheduledIntegrationEvent>().Single().ApprovedRequest);
    }

    [Fact]
    public async Task Declining_tells_the_client_by_email_why()
    {
        await Book(_olga, Monday(10));
        var appointment = Assert.Single(_appointments.All);
        appointment.ClearDomainEvents();

        var declined = await DeclineBookingRequestHandler.Handle(
            new DeclineBookingRequestCommand(_tenant, appointment.Id, _olga, "We're closed that day", false),
            _appointments,
            new CountingUnitOfWork(),
            default
        );
        var evt = appointment.DomainEvents.OfType<AppointmentBookingDeclinedDomainEvent>().Single();
        var outgoing = await AppointmentBookingDeclinedDomainEventHandler.Handle(
            evt,
            _appointments,
            new Envelope(),
            default
        );

        Assert.True(declined.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("We're closed that day", outgoing!.Reason);
        Assert.Equal(_customer, outgoing.CustomerId);
        Assert.Equal(["minato@client.test"], outgoing.Recipients.Select(r => r.Email));
    }

    private sealed class Store(InMemoryAppointments appointments, Guid[] assigned) : IPortalCalendarStore
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
                .. appointments.All.Where(a => a.BelongsToCustomer(customerId)),
            ]);

        public Task<Appointment?> FindForCustomerAsync(
            Guid tenantId,
            Guid customerId,
            Guid appointmentId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                appointments.All.FirstOrDefault(a => a.Id == appointmentId && a.BelongsToCustomer(customerId))
            );

        public Task<IReadOnlyList<Guid>> ListAssignedStaffAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>(assigned);

        public Task<Guid?> FindBookingAsync(
            Guid tenantId,
            Guid customerId,
            string idempotencyKey,
            CancellationToken ct = default
        ) => Task.FromResult<Guid?>(_bookings.TryGetValue(idempotencyKey, out var id) ? id : null);

        public void AddBooking(BookingRequest booking) => _bookings[booking.IdempotencyKey] = booking.AppointmentId;
    }

    private sealed class Policies : TaxVision.Calendar.Application.Booking.Abstractions.IBookingPolicyRepository
    {
        public BookingPolicy? Policy { get; set; }

        public Task<BookingPolicy?> GetAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Policy);

        public void Add(BookingPolicy policy) => Policy = policy;
    }

    private sealed class Contacts(Guid olga, Guid andres) : IStaffContactClient
    {
        public Task<StaffContact?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(
                userId == olga ? new StaffContact("olga@office.com", "Olga Preparer", true)
                : userId == andres ? new StaffContact("andres@office.com", "Andres Hernandez", true)
                : null
            );
    }

    private sealed class NoOffice : TaxVision.Calendar.Application.Office.Abstractions.IOfficeScheduleRepository
    {
        public Task<TaxVision.Calendar.Domain.Office.OfficeScheduleProjection?> GetAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TaxVision.Calendar.Domain.Office.OfficeScheduleProjection?>(null);

        public void Add(TaxVision.Calendar.Domain.Office.OfficeScheduleProjection projection) { }
    }

    private sealed class Rules(params AvailabilityRule[] rules)
        : TaxVision.Calendar.Application.Availability.Abstractions.IAvailabilityRepository
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
