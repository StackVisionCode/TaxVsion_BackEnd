using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Consumers;
using TaxVision.Calendar.Application.Appointments.Queries;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Feeds;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Al retirar (offboard) a un empleado: se revoca su feed token, sus citas vigentes se reasignan al
/// sucesor (con su aviso pedido en el acto) o se cancelan si no hay sucesor, y sale como asistente
/// interno de las citas vigentes. Las de otros organizadores no cambian de dueño.
/// </summary>
public sealed class CalendarUserOffboardedConsumerTests
{
    private const string NewYork = "America/New_York";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Appointment FutureAppointment(Guid organizer, bool withCustomerAttendee = false)
    {
        // Futuro relativo al reloj REAL: el consumer usa DateTime.UtcNow para el corte "vigente".
        var start = DateTime.UtcNow.AddDays(7);
        var timing = EventTiming.PointInTimeOf(start, start.AddHours(1), NewYork).Value;
        var appointment = Appointment
            .Schedule(Tenant, AppointmentTitle.Create("Client review").Value, timing, Guid.NewGuid(), organizer, Now)
            .Value;
        if (withCustomerAttendee)
            appointment.AddAttendee(
                AttendeeKind.Customer,
                null,
                Guid.NewGuid(),
                AttendeeSnapshot.Create("Client", "client@example.com").Value,
                isRequired: true,
                organizer,
                Now,
                canManageAll: false
            );
        return appointment;
    }

    private static void InviteStaff(Appointment appointment, Guid userId) =>
        Assert.True(
            appointment
                .AddAttendee(
                    AttendeeKind.InternalUser,
                    userId,
                    null,
                    AttendeeSnapshot.Create("Staff", $"{userId:N}@firm.test").Value,
                    isRequired: false,
                    appointment.OrganizerUserId,
                    Now,
                    canManageAll: false
                )
                .IsSuccess
        );

    private static async Task Run(
        FakeAppointments appointments,
        FakeFeedTokens feedTokens,
        RecordingCache cache,
        FakeMessageBus bus,
        Guid leaver,
        Guid? successor,
        FakeAvailability? availability = null
    )
    {
        await CalendarUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                SuccessorUserId = successor,
                RemovedAtUtc = Now,
            },
            appointments,
            feedTokens,
            cache,
            availability ?? new FakeAvailability(),
            new NoOpUnitOfWork(),
            NullLogger<Appointment>.Instance,
            CancellationToken.None
        );
        await CalendarDomainEventReplay.DispatchIntoAsync(bus, appointments, appointments.All);
    }

    [Fact]
    public async Task With_successor_reassigns_future_appointments_and_revokes_the_feed_token()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var appt = FutureAppointment(leaver);
        var other = FutureAppointment(Guid.NewGuid());
        var appointments = new FakeAppointments(appt, other);
        var (token, _) = CalendarFeedToken.Issue(Tenant, leaver, Now);
        var feedTokens = new FakeFeedTokens(token);
        var cache = new RecordingCache();
        var bus = new FakeMessageBus();

        await Run(appointments, feedTokens, cache, bus, leaver, successor);

        Assert.Equal(successor, appt.OrganizerUserId);
        Assert.NotEqual(successor, other.OrganizerUserId); // el de otro organizador no se toca
        Assert.False(token.IsActive); // feed token revocado
        Assert.Contains(Convert.ToHexString(token.TokenHash), cache.Removed);
        Assert.Empty(bus.Published.OfType<AppointmentCancelledIntegrationEvent>());
    }

    [Fact]
    public async Task Without_successor_cancels_future_appointments_and_notifies_attendees()
    {
        var leaver = Guid.NewGuid();
        var appt = FutureAppointment(leaver, withCustomerAttendee: true);
        var appointments = new FakeAppointments(appt);
        var (token, _) = CalendarFeedToken.Issue(Tenant, leaver, Now);
        var feedTokens = new FakeFeedTokens(token);
        var cache = new RecordingCache();
        var bus = new FakeMessageBus();

        await Run(appointments, feedTokens, cache, bus, leaver, successor: null);

        Assert.Equal(AppointmentStatus.Cancelled, appt.Status);
        var cancelled = Assert.Single(bus.Published.OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Contains(cancelled.Recipients, r => r.Email == "client@example.com");
        Assert.False(token.IsActive);
    }

    [Fact]
    public async Task Revokes_the_feed_token_even_when_there_are_no_appointments()
    {
        var leaver = Guid.NewGuid();
        var appointments = new FakeAppointments();
        var (token, _) = CalendarFeedToken.Issue(Tenant, leaver, Now);
        var feedTokens = new FakeFeedTokens(token);
        var cache = new RecordingCache();
        var bus = new FakeMessageBus();

        await Run(appointments, feedTokens, cache, bus, leaver, successor: Guid.NewGuid());

        Assert.False(token.IsActive);
        Assert.Contains(Convert.ToHexString(token.TokenHash), cache.Removed);
        Assert.Empty(bus.Published.OfType<AppointmentCancelledIntegrationEvent>());
    }

    [Fact]
    public async Task Deactivates_availability_rules_and_removes_blocks_of_the_leaver()
    {
        var leaver = Guid.NewGuid();
        var appointments = new FakeAppointments();
        var (token, _) = CalendarFeedToken.Issue(Tenant, leaver, Now);
        var feedTokens = new FakeFeedTokens(token);
        var cache = new RecordingCache();
        var bus = new FakeMessageBus();
        var rule = AvailabilityRule
            .Create(
                Tenant,
                leaver,
                DaysOfWeekMask.Weekdays,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0),
                "America/New_York",
                Now
            )
            .Value;
        var otherRule = AvailabilityRule
            .Create(
                Tenant,
                Guid.NewGuid(),
                DaysOfWeekMask.Weekdays,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0),
                "America/New_York",
                Now
            )
            .Value;
        var block = BlockedTime.Create(Tenant, leaver, Now.AddDays(1), Now.AddDays(2), "PTO", Now).Value;
        var availability = new FakeAvailability(rules: [rule, otherRule], blocks: [block]);

        await Run(appointments, feedTokens, cache, bus, leaver, successor: Guid.NewGuid(), availability);

        Assert.False(rule.IsActive); // regla del que se va desactivada
        Assert.True(otherRule.IsActive); // la de otro no se toca
        Assert.DoesNotContain(block, availability.Blocks); // bloqueo borrado
    }

    [Fact]
    public async Task With_successor_requests_the_reminder_for_the_successor_right_away()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var withReminder = FutureAppointment(leaver);
        withReminder.RequestReminder(30);
        var withoutReminder = FutureAppointment(leaver);
        var bus = new FakeMessageBus();

        await Run(
            new FakeAppointments(withReminder, withoutReminder),
            new FakeFeedTokens(),
            new RecordingCache(),
            bus,
            leaver,
            successor
        );

        // Misma clave que el job (Reminder deduplica por destinatario): es el aviso del sucesor.
        var request = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(successor, request.UserId);
        Assert.Equal(AppointmentReminderRequests.Category, request.Category);
        Assert.Equal(
            AppointmentReminderRequests.RequestKey(request.TargetId!.Value, withReminder.ReminderGeneration),
            request.RequestKey
        );
        Assert.Equal(30, request.LeadMinutes);
    }

    [Fact]
    public async Task Without_successor_requests_no_reminder()
    {
        var leaver = Guid.NewGuid();
        var appt = FutureAppointment(leaver);
        appt.RequestReminder(30);
        var bus = new FakeMessageBus();

        await Run(new FakeAppointments(appt), new FakeFeedTokens(), new RecordingCache(), bus, leaver, successor: null);

        Assert.Empty(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task Removes_the_leaver_as_staff_attendee_from_future_appointments_only()
    {
        var leaver = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        var othersMeeting = FutureAppointment(Guid.NewGuid(), withCustomerAttendee: true);
        InviteStaff(othersMeeting, leaver);
        InviteStaff(othersMeeting, colleague);
        var cancelledMeeting = FutureAppointment(Guid.NewGuid());
        InviteStaff(cancelledMeeting, leaver);
        cancelledMeeting.Cancel(cancelledMeeting.OrganizerUserId, canManageAll: false, "Moved", Now);
        var appointments = new FakeAppointments(othersMeeting, cancelledMeeting);

        await Run(
            appointments,
            new FakeFeedTokens(),
            new RecordingCache(),
            new FakeMessageBus(),
            leaver,
            Guid.NewGuid()
        );

        Assert.DoesNotContain(othersMeeting.Attendees, a => a.UserId == leaver);
        Assert.Contains(othersMeeting.Attendees, a => a.UserId == colleague); // el resto queda
        Assert.Contains(othersMeeting.Attendees, a => a.Kind == AttendeeKind.Customer);
        Assert.Contains(cancelledMeeting.Attendees, a => a.UserId == leaver); // historial intacto
    }

    [Fact]
    public async Task Reassigned_appointment_drops_the_leaver_as_attendee_too()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var appt = FutureAppointment(leaver);
        InviteStaff(appt, leaver);

        await Run(
            new FakeAppointments(appt),
            new FakeFeedTokens(),
            new RecordingCache(),
            new FakeMessageBus(),
            leaver,
            successor
        );

        Assert.Equal(successor, appt.OrganizerUserId);
        Assert.DoesNotContain(appt.Attendees, a => a.UserId == leaver);
    }

    [Fact]
    public async Task Without_successor_keeps_the_leaver_on_the_appointments_it_cancels()
    {
        var leaver = Guid.NewGuid();
        var appt = FutureAppointment(leaver);
        InviteStaff(appt, leaver);

        await Run(
            new FakeAppointments(appt),
            new FakeFeedTokens(),
            new RecordingCache(),
            new FakeMessageBus(),
            leaver,
            successor: null
        );

        Assert.Equal(AppointmentStatus.Cancelled, appt.Status);
        Assert.Contains(appt.Attendees, a => a.UserId == leaver); // cancelada = historial
    }

    [Fact]
    public async Task CancelledSeriesOfLeaver_ClosesEveryOccurrence()
    {
        var leaver = Guid.NewGuid();
        var monday = ReminderTargetsTestData.Next(DayOfWeek.Monday);
        var series = ReminderTargetsTestData.Weekly(Tenant, leaver, monday, leadMinutes: 30);
        var expected = ReminderTargetsTestData.WeeklyTargets(series.Id, monday);
        var bus = new FakeMessageBus();

        await Run(
            new FakeAppointments(series),
            new FakeFeedTokens(),
            new RecordingCache(),
            bus,
            leaver,
            successor: null
        );

        Assert.Equal(AppointmentStatus.Cancelled, series.Status);
        var closed = bus.Published.OfType<ReminderTargetClosedIntegrationEvent>().Select(c => c.TargetId).ToList();
        Assert.Equal(expected.Count, closed.Count);
        Assert.Equal(expected, closed.ToHashSet());
    }

    [Fact]
    public async Task TwoActiveFeedTokens_BothRevoked()
    {
        var leaver = Guid.NewGuid();
        var (first, _) = CalendarFeedToken.Issue(Tenant, leaver, Now);
        var (second, _) = CalendarFeedToken.Issue(Tenant, leaver, Now.AddMinutes(1));
        var cache = new RecordingCache();

        await Run(
            new FakeAppointments(),
            new FakeFeedTokens(first, second),
            cache,
            new FakeMessageBus(),
            leaver,
            successor: null
        );

        Assert.False(first.IsActive);
        Assert.False(second.IsActive);
        Assert.Contains(Convert.ToHexString(first.TokenHash), cache.Removed);
        Assert.Contains(Convert.ToHexString(second.TokenHash), cache.Removed);
    }

    private sealed class FakeAvailability(
        IEnumerable<AvailabilityRule>? rules = null,
        IEnumerable<BlockedTime>? blocks = null
    ) : IAvailabilityRepository
    {
        public List<AvailabilityRule> Rules { get; } = rules?.ToList() ?? [];
        public List<BlockedTime> Blocks { get; } = blocks?.ToList() ?? [];

        public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<AvailabilityRule>>(
                Rules.Where(r => r.TenantId == tenantId && r.UserId == userId && r.IsActive).ToList()
            );

        public Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<BlockedTime>>(
                Blocks
                    .Where(b =>
                        b.TenantId == tenantId && b.UserId == userId && b.StartUtc < toUtc && b.EndUtc > fromUtc
                    )
                    .ToList()
            );

        public Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<BlockedTime>>(
                Blocks.Where(b => b.TenantId == tenantId && b.UserId == userId).ToList()
            );

        public void AddRule(AvailabilityRule rule) => Rules.Add(rule);

        public void AddBlock(BlockedTime block) => Blocks.Add(block);

        public void RemoveBlock(BlockedTime block) => Blocks.Remove(block);
    }

    [Fact]
    public async Task Offboarding_impact_counts_future_appointments_the_employee_organizes()
    {
        var leaver = Guid.NewGuid();
        var appointments = new FakeAppointments(
            FutureAppointment(leaver),
            FutureAppointment(leaver),
            FutureAppointment(Guid.NewGuid())
        );

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, leaver, DateTime.UtcNow),
            appointments,
            CancellationToken.None
        );

        Assert.Equal(2, result.FutureAppointments);
    }

    private sealed class FakeAppointments : IAppointmentRepository
    {
        private readonly List<Appointment> _all;

        public FakeAppointments(params Appointment[] seed)
        {
            // Lo sembrado ya está guardado: sus eventos ya salieron.
            foreach (var appointment in seed)
                appointment.ClearDomainEvents();
            _all = [.. seed];
        }

        public IReadOnlyList<Appointment> All => _all;

        public Task<IReadOnlyList<Appointment>> ListFutureByOrganizerAsync(
            Guid tenantId,
            Guid organizerUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<Appointment>>(
                _all.Where(a =>
                        a.TenantId == tenantId
                        && a.OrganizerUserId == organizerUserId
                        && a.Status != AppointmentStatus.Cancelled
                        && (a.Recurrence != null || a.Timing.EndUtc > nowUtc)
                    )
                    .ToList()
            );

        public Task<IReadOnlyList<Appointment>> ListFutureByStaffAttendeeAsync(
            Guid tenantId,
            Guid userId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<Appointment>>(
                _all.Where(a =>
                        a.TenantId == tenantId
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Attendees.Any(at => at.UserId == userId)
                        && (a.Recurrence != null || a.Timing.EndUtc > nowUtc)
                    )
                    .ToList()
            );

        public Task<Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.Find(a => a.TenantId == tenantId && a.Id == appointmentId) is { } found
                    ? Result.Success(found)
                    : Result.Failure<Appointment>(AppointmentErrors.NotFound)
            );

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> CountFutureByOrganizerAsync(
            Guid tenantId,
            Guid organizerUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.Count(a =>
                    a.TenantId == tenantId
                    && a.OrganizerUserId == organizerUserId
                    && a.Status != AppointmentStatus.Cancelled
                    && (a.Recurrence != null || a.Timing.EndUtc > nowUtc)
                )
            );

        public Task<Result<Appointment>> GetByIdForReadAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public void Add(Appointment appointment) => _all.Add(appointment);

        public void Remove(Appointment appointment) => _all.Remove(appointment);

        public void DiscardPendingChanges() { }
    }

    private sealed class FakeFeedTokens(params CalendarFeedToken[] seed) : ICalendarFeedTokenRepository
    {
        private readonly List<CalendarFeedToken> _all = [.. seed];

        public Task<CalendarFeedToken?> FindActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(_all.Find(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive));

        public Task<IReadOnlyList<CalendarFeedToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<CalendarFeedToken>>(
                _all.FindAll(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive)
            );

        public Task<CalendarFeedToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default) =>
            Task.FromResult(_all.Find(t => t.TokenHash.AsSpan().SequenceEqual(tokenHash)));

        public void Add(CalendarFeedToken token) => _all.Add(token);
    }

    private sealed class RecordingCache : ICalendarFeedCache
    {
        public List<string> Removed { get; } = [];

        public Task<string?> GetAsync(string tokenHashHex, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string tokenHashHex, string ics, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(string tokenHashHex, CancellationToken ct = default)
        {
            Removed.Add(tokenHashHex);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
