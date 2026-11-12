using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Queries;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>B1 (Calendar): el rango que pinta el calendario se valida, se ordena y trae lo necesario para mostrar y enlazar.</summary>
public sealed class AppointmentRangeQueryTests
{
    private const string NewYork = "America/New_York";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static IOptions<CalendarVisibilityOptions> NoVisibility() =>
        Options.Create(new CalendarVisibilityOptions { Enabled = false });

    private static AppointmentType Type() =>
        AppointmentType.Create(Tenant, "Client review", TimeSpan.FromHours(1), "#3366FF", Now, isVirtual: true).Value;

    private static Appointment At(DateTime startUtc, Guid organizer, Guid typeId)
    {
        var timing = EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), NewYork).Value;
        var appointment = Appointment
            .Schedule(Tenant, AppointmentTitle.Create("Review").Value, timing, typeId, organizer, Now)
            .Value;
        appointment.ClearDomainEvents();
        return appointment;
    }

    private static GetAppointmentRangeQuery Query(DateTime from, DateTime to, Guid? participant = null) =>
        new(Tenant, from, to, OrganizerUserId: null, ActorUserId: Guid.NewGuid(), CanViewAll: true, participant);

    private static Task<Result<IReadOnlyList<OccurrenceResponse>>> Run(
        GetAppointmentRangeQuery query,
        AppointmentType type,
        params Appointment[] seed
    ) =>
        GetAppointmentRangeHandler.Handle(
            query,
            new SeededRange(seed),
            new SingleTypeRepository(type),
            new NullCalendarMetrics(),
            NoVisibility(),
            default
        );

    [Fact]
    public async Task Inverted_range_is_rejected()
    {
        var result = await Run(Query(From, From.AddDays(-1)), Type());

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentErrors.RangeInvalid, result.Error);
    }

    [Fact]
    public async Task Range_wider_than_62_days_is_rejected()
    {
        var result = await Run(Query(From, From.AddDays(63)), Type());

        Assert.True(result.IsFailure);
        Assert.Equal(AppointmentErrors.RangeInvalid, result.Error);
    }

    [Fact]
    public async Task Participant_filter_includes_the_appointments_where_the_user_attends()
    {
        var type = Type();
        var user = Guid.NewGuid();
        var attending = At(From.AddHours(15), Guid.NewGuid(), type.Id);
        Assert.True(
            attending
                .AddAttendee(
                    AttendeeKind.InternalUser,
                    user,
                    null,
                    AttendeeSnapshot.Create("Ana", "ana@example.com").Value,
                    isRequired: true,
                    attending.OrganizerUserId,
                    Now,
                    canManageAll: false
                )
                .IsSuccess
        );
        var organizing = At(From.AddHours(17), user, type.Id);
        var unrelated = At(From.AddHours(19), Guid.NewGuid(), type.Id);

        var result = await Run(Query(From, From.AddDays(1), user), type, attending, organizing, unrelated);

        Assert.True(result.IsSuccess);
        Assert.Equal([attending.Id, organizing.Id], result.Value.Select(o => o.AppointmentId));
    }

    [Fact]
    public async Task Occurrences_are_ordered_and_carry_type_status_and_meeting()
    {
        var type = Type();
        var organizer = Guid.NewGuid();
        var later = At(From.AddHours(20), organizer, type.Id);
        var earlier = At(From.AddHours(14), organizer, type.Id);
        earlier.LinkMeeting(Guid.NewGuid(), "ABC123");

        var result = await Run(Query(From, From.AddDays(1)), type, later, earlier);

        Assert.True(result.IsSuccess);
        var first = result.Value[0];
        Assert.Equal(earlier.Id, first.AppointmentId);
        Assert.Equal(later.Id, result.Value[1].AppointmentId);
        Assert.Equal(type.Id, first.AppointmentTypeId);
        Assert.Equal("Client review", first.TypeName);
        Assert.Equal("#3366FF", first.ColorHex);
        Assert.Equal("Confirmed", first.Status);
        Assert.Equal(organizer, first.OrganizerUserId);
        Assert.Equal(earlier.MeetingId, first.MeetingId);
        Assert.Equal("ABC123", first.MeetingShortCode);
        Assert.False(first.IsRecurring);
    }

    /// <summary>Devuelve lo sembrado como si la consulta SQL ya hubiera filtrado por solape.</summary>
    private sealed class SeededRange(params Appointment[] seed) : IAppointmentRepository
    {
        public Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>(seed);

        public Task<IReadOnlyList<Appointment>> ListOrganizerTypeForRangeAsync(
            Guid tenantId,
            Guid organizerUserId,
            Guid appointmentTypeId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

        public Task<Result<Appointment>> GetByIdForReadAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<Appointment>(AppointmentErrors.NotFound));

        public Task<Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<Appointment>(AppointmentErrors.NotFound));

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>(seed);

        public Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

        public Task<IReadOnlyList<Appointment>> ListFutureByOrganizerAsync(
            Guid tenantId,
            Guid organizerUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

        public Task<IReadOnlyList<Appointment>> ListFutureByStaffAttendeeAsync(
            Guid tenantId,
            Guid userId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

        public Task<int> CountFutureByOrganizerAsync(
            Guid tenantId,
            Guid organizerUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public void Add(Appointment appointment) { }

        public void Remove(Appointment appointment) { }

        public void DiscardPendingChanges() { }
    }
}
