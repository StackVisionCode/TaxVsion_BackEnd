using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Al retirar (offboard) a un empleado: se revoca su feed token y sus citas vigentes se reasignan al
/// sucesor, o se cancelan si no hay sucesor. Las de otros organizadores no se tocan.
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
                Now
            );
        return appointment;
    }

    private static Task Run(
        FakeAppointments appointments,
        FakeFeedTokens feedTokens,
        RecordingCache cache,
        FakeMessageBus bus,
        Guid leaver,
        Guid? successor,
        FakeAvailability? availability = null
    ) =>
        CalendarUserOffboardedConsumer.Handle(
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
            bus,
            new NoOpCorrelationContext(),
            NullLogger<Appointment>.Instance,
            CancellationToken.None
        );

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

    private sealed class FakeAppointments(params Appointment[] seed) : IAppointmentRepository
    {
        private readonly List<Appointment> _all = [.. seed];

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

        public Task<Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

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

        public void Add(Appointment appointment) => _all.Add(appointment);

        public void Remove(Appointment appointment) => _all.Remove(appointment);
    }

    private sealed class FakeFeedTokens(CalendarFeedToken? seed = null) : ICalendarFeedTokenRepository
    {
        private readonly List<CalendarFeedToken> _all = seed is null ? [] : [seed];

        public Task<CalendarFeedToken?> FindActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(_all.Find(t => t.TenantId == tenantId && t.UserId == userId && t.IsActive));

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

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
