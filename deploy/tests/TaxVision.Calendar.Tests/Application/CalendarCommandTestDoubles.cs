using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Feeds;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>En memoria no hay carrera que serializar.</summary>
internal sealed class NoScheduleLock : IOrganizerScheduleLock
{
    public Task AcquireAsync(Guid tenantId, Guid organizerUserId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Repositorio en memoria para los handlers de citas. No informa choques de agenda.</summary>
internal sealed class InMemoryAppointments : IAppointmentRepository
{
    public InMemoryAppointments(params Appointment[] seed)
    {
        // Lo sembrado ya está guardado: sus eventos ya salieron.
        foreach (var appointment in seed)
            appointment.ClearDomainEvents();
        All = [.. seed];
    }

    public List<Appointment> All { get; }

    public Task<Result<Appointment>> GetByIdAsync(Guid tenantId, Guid appointmentId, CancellationToken ct = default) =>
        Task.FromResult(
            All.Find(a => a.TenantId == tenantId && a.Id == appointmentId) is { } found
                ? Result.Success(found)
                : Result.Failure<Appointment>(AppointmentErrors.NotFound)
        );

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
    ) =>
        Task.FromResult<IReadOnlyList<Appointment>>(
            All.FindAll(a =>
                a.TenantId == tenantId
                && a.OrganizerUserId == organizerUserId
                && a.Status != AppointmentStatus.Cancelled
            )
        );

    public Task<IReadOnlyList<Appointment>> ListFutureByStaffAttendeeAsync(
        Guid tenantId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<Appointment>>([]);

    // En memoria no hay proyección de asignación: lectura sin filtro.
    public Task<Result<Appointment>> GetByIdForReadAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    ) => GetByIdAsync(tenantId, appointmentId, ct);

    public Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        Guid? assignedToUserId,
        CancellationToken ct = default
    ) => ListForRangeAsync(tenantId, rangeStartUtc, rangeEndUtc, ct);

    public Task<int> CountFutureByOrganizerAsync(
        Guid tenantId,
        Guid organizerUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) => Task.FromResult(0);

    public void Add(Appointment appointment) => All.Add(appointment);

    public void Remove(Appointment appointment) => All.Remove(appointment);

    public void DiscardPendingChanges() { }
}

internal sealed class SingleTypeRepository(AppointmentType type) : IAppointmentTypeRepository
{
    public Task<Result<AppointmentType>> GetByIdAsync(Guid tenantId, Guid typeId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(type));

    public Task<IReadOnlyList<AppointmentType>> ListAsync(
        Guid tenantId,
        bool onlyActive,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<AppointmentType>>([type]);

    public Task<bool> AnyAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(true);

    public void Add(AppointmentType added) { }
}

internal sealed class EmptyAvailability : IAvailabilityRepository
{
    public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<AvailabilityRule>>([]);

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

    public void AddRule(AvailabilityRule rule) { }

    public void AddBlock(BlockedTime block) { }

    public void RemoveBlock(BlockedTime block) { }
}

internal sealed class NoFeedTokens : ICalendarFeedTokenRepository
{
    public Task<CalendarFeedToken?> FindActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<CalendarFeedToken?>(null);

    public Task<IReadOnlyList<CalendarFeedToken>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<CalendarFeedToken>>([]);

    public Task<CalendarFeedToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default) =>
        Task.FromResult<CalendarFeedToken?>(null);

    public void Add(CalendarFeedToken token) { }
}

internal sealed class NoFeedCache : ICalendarFeedCache
{
    public Task<string?> GetAsync(string tokenHashHex, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task SetAsync(string tokenHashHex, string ics, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(string tokenHashHex, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Cuenta los guardados: sirve para afirmar que se publicó después de guardar.</summary>
internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.FromResult(0);
    }
}

internal sealed class NullCalendarMetrics : ICalendarMetrics
{
    public void RecordCreated(bool isRecurring) { }

    public void RecordRescheduled(bool isRecurring) { }

    public void RecordCancelled(bool isRecurring) { }

    public void RecordExpansionDuration(double milliseconds, int seriesCount) { }

    public void RecordConflictDetected(bool blocked) { }

    public void RecordIcsFeedRequest(bool found) { }

    public void RecordIcsFeedStale() { }
}

internal sealed class FixedCorrelation : ICorrelationContext
{
    public string CorrelationId { get; private set; } = "test";

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId) => new Scope();

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }
}
