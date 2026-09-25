using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Queries;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Visibilidad por asignación (P2): los handlers de lectura solo restringen al actor cuando el flag está
/// encendido Y el actor NO ve todo (customers.view_all). En cualquier otro caso pasan null al repositorio
/// (= sin restricción), preservando el comportamiento previo hasta sembrar la proyección.
/// </summary>
public sealed class AppointmentVisibilityFilterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private static IOptions<CalendarVisibilityOptions> Options(bool enabled) =>
        Microsoft.Extensions.Options.Options.Create(new CalendarVisibilityOptions { Enabled = enabled });

    [Theory]
    [InlineData(true, false, true)] // flag ON + no ve todo  → restringe al actor
    [InlineData(true, true, false)] // flag ON + ve todo     → sin restricción
    [InlineData(false, false, false)] // flag OFF            → sin restricción
    [InlineData(false, true, false)] // flag OFF + ve todo   → sin restricción
    public async Task Range_restringe_al_actor_solo_con_flag_on_y_sin_view_all(
        bool enabled,
        bool canViewAll,
        bool shouldRestrict
    )
    {
        var repo = new CapturingRepository();
        var query = new GetAppointmentRangeQuery(
            Tenant,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            OrganizerUserId: null,
            ActorUserId: Actor,
            CanViewAll: canViewAll
        );

        var result = await GetAppointmentRangeHandler.Handle(query, repo, new NoOpMetrics(), Options(enabled), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(shouldRestrict ? Actor : (Guid?)null, repo.LastRangeAssignee);
    }

    [Fact]
    public async Task Detail_restringe_al_actor_con_flag_on_y_sin_view_all()
    {
        var repo = new CapturingRepository();
        var query = new GetAppointmentByIdQuery(Tenant, Guid.NewGuid(), Actor, CanViewAll: false);

        await GetAppointmentByIdHandler.Handle(query, repo, Options(enabled: true), default);

        Assert.Equal(Actor, repo.LastDetailAssignee);
    }

    [Fact]
    public async Task Detail_no_restringe_con_view_all()
    {
        var repo = new CapturingRepository();
        var query = new GetAppointmentByIdQuery(Tenant, Guid.NewGuid(), Actor, CanViewAll: true);

        await GetAppointmentByIdHandler.Handle(query, repo, Options(enabled: true), default);

        Assert.Null(repo.LastDetailAssignee);
    }

    private sealed class CapturingRepository : IAppointmentRepository
    {
        public Guid? LastRangeAssignee { get; private set; }
        public Guid? LastDetailAssignee { get; private set; }

        public Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            LastRangeAssignee = assignedToUserId;
            return Task.FromResult<IReadOnlyList<Appointment>>([]);
        }

        public Task<Result<Appointment>> GetByIdForReadAsync(
            Guid tenantId,
            Guid appointmentId,
            Guid? assignedToUserId,
            CancellationToken ct = default
        )
        {
            LastDetailAssignee = assignedToUserId;
            return Task.FromResult(Result.Failure<Appointment>(AppointmentErrors.NotFound));
        }

        // Miembros no ejercidos por estas pruebas.
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
        ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

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

        public void Add(Appointment appointment) { }

        public void Remove(Appointment appointment) { }
    }

    private sealed class NoOpMetrics : ICalendarMetrics
    {
        public void RecordCreated(bool isRecurring) { }

        public void RecordRescheduled(bool isRecurring) { }

        public void RecordCancelled(bool isRecurring) { }

        public void RecordExpansionDuration(double milliseconds, int seriesCount) { }

        public void RecordConflictDetected(bool blocked) { }

        public void RecordIcsFeedRequest(bool found) { }

        public void RecordIcsFeedStale() { }
    }
}
