using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Work;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Work Profile (Calendar): próximas citas de una persona como organizador o asistente, con "esta semana" en la
/// zona de la oficina, la visibilidad del que mira y nunca de otra oficina.
/// </summary>
public sealed class AppointmentWorkTests
{
    private const string NewYork = "America/New_York";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bruno = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();

    // Miércoles 18 de noviembre de 2026, 10:00 en Nueva York.
    private static readonly DateTime Now = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone Office = ResolvedTimeZone.Resolve(NewYork);

    [Fact]
    public async Task Counts_upcoming_appointments_the_person_organizes_or_attends()
    {
        var organized = At(Now.AddDays(1), Ana);
        var attended = At(Now.AddDays(3), Bruno, attendee: Ana);
        var later = At(Now.AddDays(20), Ana);
        var someoneElse = At(Now.AddDays(2), Bruno);
        var inProgress = At(Now.AddMinutes(-30), Ana);
        var cancelled = At(Now.AddDays(1), Ana);
        Assert.True(cancelled.Cancel(Ana, canManageAll: false, null, Now).IsSuccess);

        var work = await Summary(
            Query(Ana),
            new InMemoryAppointments(organized, attended, later, someoneElse, inProgress, cancelled)
        );

        Assert.Equal("calendar", work.Provider);
        Assert.Equal("calendar", work.Module);
        var count = Assert.Single(work.Counts);
        Assert.Equal(new WorkCount(WorkKinds.Appointments, 3, 0, 2, 0), count);
        Assert.Equal([organized.Id, attended.Id, later.Id], work.Top.Select(i => i.Id));
        var first = work.Top[0];
        Assert.Equal(WorkKinds.Appointments, first.Kind);
        Assert.Equal("Review", first.Title);
        Assert.Equal(Now.AddDays(1), first.DueAtUtc);
        Assert.Null(first.DueDate);
        Assert.Equal("Confirmed", first.Status);
        Assert.Equal($"/calendar?date=2026-11-19&appointmentId={organized.Id}", first.Link);
    }

    [Fact]
    public async Task A_weekly_series_counts_each_upcoming_occurrence_and_appears_once_in_top()
    {
        var series = MondaysAtNine(Ana);

        var work = await Summary(Query(Ana), new InMemoryAppointments(series));

        // Lunes del 23-nov-2026 al 18-ene-2027 (horizonte de 62 días); solo el 23 cae en esta semana.
        var count = Assert.Single(work.Counts);
        Assert.Equal(9, count.Open);
        Assert.Equal(1, count.DueSoon);
        var item = Assert.Single(work.Top);
        Assert.Equal(new DateTime(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc), item.DueAtUtc);
    }

    [Fact]
    public async Task Top_keeps_the_five_soonest()
    {
        var seed = Enumerable.Range(1, 7).Select(d => At(Now.AddDays(d), Ana)).ToArray();

        var work = await Summary(Query(Ana), new InMemoryAppointments(seed));

        Assert.Equal(7, work.Counts[0].Open);
        Assert.Equal(seed.Take(WorkLimits.TopItems).Select(a => a.Id), work.Top.Select(i => i.Id));
    }

    [Fact]
    public async Task Due_soon_ends_at_the_office_midnight_seven_days_out()
    {
        // 24-nov 22:00 en Nueva York, ya 25-nov en UTC.
        var edge = At(new DateTime(2026, 11, 25, 3, 0, 0, DateTimeKind.Utc), Ana);

        var inNewYork = await Summary(Query(Ana), new InMemoryAppointments(edge));
        var inUtc = await Summary(
            Query(Ana) with
            {
                OfficeZone = ResolvedTimeZone.Utc,
            },
            new InMemoryAppointments(edge)
        );

        Assert.Equal(1, inNewYork.Counts[0].DueSoon);
        Assert.Equal(0, inUtc.Counts[0].DueSoon);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Counts_use_the_viewers_assignment_visibility(bool enabled, bool canViewAll, bool restricted)
    {
        var repository = new CapturingAppointments(new InMemoryAppointments(At(Now.AddDays(1), Ana)));

        await GetAppointmentWorkHandler.Handle(
            Query(Ana) with
            {
                CanViewAll = canViewAll,
            },
            repository,
            Visibility(enabled),
            default
        );
        await GetAppointmentWorkBatchHandler.Handle(
            BatchQuery([Ana]) with
            {
                CanViewAll = canViewAll,
            },
            repository,
            Visibility(enabled),
            default
        );

        Assert.Equal([restricted ? Viewer : null, restricted ? Viewer : null], repository.Assignees);
    }

    [Fact]
    public async Task Another_offices_appointments_never_count()
    {
        var elsewhere = Appointment
            .Schedule(
                Guid.NewGuid(),
                AppointmentTitle.Create("Review").Value,
                EventTiming.PointInTimeOf(Now.AddDays(1), Now.AddDays(1).AddHours(1), NewYork).Value,
                Guid.NewGuid(),
                Ana,
                Now.AddDays(-1)
            )
            .Value;

        var work = await Summary(Query(Ana), new InMemoryAppointments(elsewhere));
        var batch = await Batch(BatchQuery([Ana]), new InMemoryAppointments(elsewhere));

        Assert.Equal(0, work.Counts[0].Open);
        Assert.Empty(work.Top);
        Assert.Equal(0, batch.Rows[0].Open);
    }

    [Fact]
    public async Task Batch_counts_the_next_seven_office_days_per_person_in_the_requested_order()
    {
        var carla = Guid.NewGuid();
        var appointments = new InMemoryAppointments(
            At(Now.AddDays(1), Ana),
            At(Now.AddDays(2), Ana),
            At(Now.AddDays(20), Ana),
            At(Now.AddDays(3), Ana, attendee: Bruno)
        );

        var batch = await Batch(BatchQuery([carla, Bruno, Ana]), appointments);

        Assert.Equal(
            [new WorkBatchRow(carla, 0, 0), new WorkBatchRow(Bruno, 1, 0), new WorkBatchRow(Ana, 3, 0)],
            batch.Rows
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData(",")]
    public void Batch_user_ids_must_be_valid(string? raw)
    {
        Assert.Equal(WorkErrors.InvalidUserIds, WorkUserIds.Parse(raw).Error);
    }

    [Fact]
    public void Batch_user_ids_are_capped_and_deduplicated()
    {
        var many = string.Join(',', Enumerable.Range(0, WorkLimits.BatchUsers + 1).Select(_ => Guid.NewGuid()));

        Assert.True(WorkUserIds.Parse(many).IsFailure);
        Assert.Equal([Ana, Bruno], WorkUserIds.Parse($"{Ana}, {Bruno},{Ana}").Value);
    }

    [Fact]
    public async Task Items_page_every_upcoming_occurrence_the_open_count_counts()
    {
        var single = At(Now.AddDays(1), Ana);
        var appointments = new InMemoryAppointments(MondaysAtNine(Ana), single);

        var work = await Summary(Query(Ana), appointments);
        var first = await Items(ItemsQuery(Ana, page: 1, size: 4), appointments);
        var last = await Items(ItemsQuery(Ana, page: 3, size: 4), appointments);

        Assert.Equal(work.Counts[0].Open, first.TotalCount);
        Assert.Equal(10, first.TotalCount);
        Assert.Equal((1, 4, 4), (first.Page, first.Size, first.Items.Count));
        Assert.Equal(2, last.Items.Count);
        Assert.Equal(single.Id, first.Items[0].Id);
        Assert.Equal(work.Top[0], first.Items[0]);
        var starts = first.Items.Concat(last.Items).Select(i => i.DueAtUtc!.Value).ToList();
        Assert.Equal(starts.Order(), starts);
        Assert.Equal(new DateTime(2027, 1, 18, 14, 0, 0, DateTimeKind.Utc), starts[^1]);
    }

    [Fact]
    public async Task Items_use_the_viewers_assignment_visibility()
    {
        var repository = new CapturingAppointments(new InMemoryAppointments(At(Now.AddDays(1), Ana)));

        await GetAppointmentWorkItemsHandler.Handle(
            ItemsQuery(Ana, 1, 20) with
            {
                CanViewAll = false,
            },
            repository,
            Visibility(true),
            default
        );

        Assert.Equal([Viewer], repository.Assignees);
    }

    private static GetAppointmentWorkItemsQuery ItemsQuery(Guid userId, int page, int size) =>
        new(Tenant, userId, Viewer, CanViewAll: true, Office, Now, page, size);

    private static async Task<BuildingBlocks.Common.PagedResult<WorkItem>> Items(
        GetAppointmentWorkItemsQuery query,
        IAppointmentRepository appointments
    )
    {
        var result = await GetAppointmentWorkItemsHandler.Handle(query, appointments, Visibility(true), default);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static GetAppointmentWorkQuery Query(Guid userId) =>
        new(Tenant, userId, Viewer, CanViewAll: true, Office, Now);

    private static GetAppointmentWorkBatchQuery BatchQuery(IReadOnlyList<Guid> userIds) =>
        new(Tenant, userIds, Viewer, CanViewAll: true, Office, Now);

    private static IOptions<CalendarVisibilityOptions> Visibility(bool enabled) =>
        Options.Create(new CalendarVisibilityOptions { Enabled = enabled });

    private static async Task<WorkSummaryResponse> Summary(
        GetAppointmentWorkQuery query,
        IAppointmentRepository appointments
    )
    {
        var result = await GetAppointmentWorkHandler.Handle(query, appointments, Visibility(true), default);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task<WorkBatchResponse> Batch(
        GetAppointmentWorkBatchQuery query,
        IAppointmentRepository appointments
    )
    {
        var result = await GetAppointmentWorkBatchHandler.Handle(query, appointments, Visibility(true), default);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static Appointment At(DateTime startUtc, Guid organizer, Guid? attendee = null)
    {
        var appointment = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Review").Value,
                EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), NewYork).Value,
                Guid.NewGuid(),
                organizer,
                Now.AddDays(-1)
            )
            .Value;
        if (attendee is { } staff)
            Assert.True(
                appointment
                    .AddAttendee(
                        AttendeeKind.InternalUser,
                        staff,
                        null,
                        AttendeeSnapshot.Create("Staff", "staff@firm.test").Value,
                        isRequired: true,
                        organizer,
                        Now.AddDays(-1),
                        canManageAll: false
                    )
                    .IsSuccess
            );
        return appointment;
    }

    private static Appointment MondaysAtNine(Guid organizer)
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 11, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;
        var series = Appointment
            .Schedule(Tenant, AppointmentTitle.Create("Weekly").Value, timing, Guid.NewGuid(), organizer, Now)
            .Value;
        Assert.True(
            series
                .MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, timing, organizer, false)
                .IsSuccess
        );
        return series;
    }

    /// <summary>Anota con qué visibilidad se pidió el rango.</summary>
    private sealed class CapturingAppointments(IAppointmentRepository inner) : IAppointmentRepository
    {
        public List<Guid?> Assignees { get; } = [];

        public Task<IReadOnlyList<Appointment>> ListVisibleForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            Assignees.Add(assignedToUserId);
            return inner.ListVisibleForUserRangeAsync(
                tenantId,
                userId,
                rangeStartUtc,
                rangeEndUtc,
                assignedToUserId,
                ct
            );
        }

        public Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            Assignees.Add(assignedToUserId);
            return inner.ListVisibleForRangeAsync(tenantId, rangeStartUtc, rangeEndUtc, assignedToUserId, ct);
        }

        public Task<Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) => inner.GetByIdAsync(tenantId, appointmentId, ct);

        public Task<Result<Appointment>> GetByIdForReadAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid? assignedToUserId,
            CancellationToken ct = default
        ) => inner.GetByIdForReadAsync(tenantId, appointmentId, assignedToUserId, ct);

        public Task<IReadOnlyList<Appointment>> ListOrganizerTypeForRangeAsync(
            Guid tenantId,
            Guid organizerUserId,
            Guid appointmentTypeId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) =>
            inner.ListOrganizerTypeForRangeAsync(
                tenantId,
                organizerUserId,
                appointmentTypeId,
                rangeStartUtc,
                rangeEndUtc,
                ct
            );

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => inner.ListForRangeAsync(tenantId, rangeStartUtc, rangeEndUtc, ct);

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
}
