using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Availability.Queries;
using TaxVision.Calendar.Application.Booking.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.Portal.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>
/// La única fuente de "qué horarios se pueden reservar": la lista del portal, la reserva y el cambio
/// de hora usan esto mismo, así que lo que el cliente ve es lo que se acepta.
/// </summary>
internal static class BookingSlotFinder
{
    public const int MaxRangeDays = 14;

    /// <summary><c>Preparers</c>: el elegido, o todos los asignados con "Any available".</summary>
    public sealed record Bookable(
        BookingPolicy Policy,
        AppointmentType Type,
        string TimeZoneId,
        IReadOnlyList<Guid> Preparers
    );

    /// <summary>
    /// Reserva activa, tipo ofrecido y activo, y preparador asignado al cliente. Sin preparador
    /// ("Any available") solo cuando la oficina aprueba cada reserva: ella asigna al aprobar.
    /// </summary>
    public static async Task<Result<Bookable>> CheckAsync(
        Guid tenantId,
        Guid customerId,
        Guid typeId,
        Guid? preparerUserId,
        IPortalCalendarStore store,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IOfficeScheduleRepository offices,
        IBookingPolicyRepository policies,
        CancellationToken ct
    )
    {
        var policy = await policies.GetAsync(tenantId, ct);
        if (policy is not { Enabled: true } || !policy.BookableTypeIds.Contains(typeId))
            return Result.Failure<Bookable>(BookingErrors.NotAvailable);

        var type = await types.GetByIdAsync(tenantId, typeId, ct);
        if (type.IsFailure || !type.Value.IsActive)
            return Result.Failure<Bookable>(BookingErrors.NotAvailable);

        var assigned = await store.ListAssignedStaffAsync(tenantId, customerId, ct);
        IReadOnlyList<Guid> preparers;
        if (preparerUserId is { } chosen)
        {
            if (!assigned.Contains(chosen))
                return Result.Failure<Bookable>(BookingErrors.NotAvailable);
            preparers = [chosen];
        }
        else
        {
            if (!policy.RequiresApproval || assigned.Count == 0)
                return Result.Failure<Bookable>(BookingErrors.NotAvailable);
            preparers = assigned;
        }

        var zone = await TimeZoneAsync(tenantId, preparers[0], availability, offices, ct);
        return Result.Success(new Bookable(policy, type.Value, zone, preparers));
    }

    /// <summary>Zona para rotular: la de la oficina, la del horario del preparador o UTC.</summary>
    public static async Task<string> TimeZoneAsync(
        Guid tenantId,
        Guid preparerUserId,
        IAvailabilityRepository availability,
        IOfficeScheduleRepository offices,
        CancellationToken ct
    )
    {
        var office = await offices.GetAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(office?.TimeZoneId))
            return office.TimeZoneId;
        var rules = await availability.ListRulesAsync(tenantId, preparerUserId, ct);
        return rules.Count > 0 ? rules[0].TimeZone.Id : "UTC";
    }

    /// <summary>Los horarios reservables del rango con cualquiera de los preparadores, sin repetir.</summary>
    public static async Task<Result<IReadOnlyList<TimeSlot>>> FindAsync(
        Guid tenantId,
        Bookable bookable,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? excludeAppointmentId,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        IAppointmentTypeRepository types,
        IOfficeScheduleRepository offices,
        CancellationToken ct
    )
    {
        var byStart = new SortedDictionary<DateTime, TimeSlot>();
        foreach (var preparer in bookable.Preparers)
        {
            var slots = await FindForAsync(
                tenantId,
                preparer,
                bookable,
                fromUtc,
                toUtc,
                excludeAppointmentId,
                appointments,
                availability,
                types,
                offices,
                ct
            );
            if (slots.IsFailure)
                return slots;
            foreach (var slot in slots.Value)
                byStart.TryAdd(slot.StartUtc, slot);
        }

        return Result.Success<IReadOnlyList<TimeSlot>>([.. byStart.Values]);
    }

    /// <summary>¿Este comienzo es uno de los horarios que se ofrecerían ahora mismo con ese preparador?</summary>
    public static async Task<bool> IsFreeAsync(
        Guid tenantId,
        Guid preparerUserId,
        Bookable bookable,
        DateTime startUtc,
        Guid? excludeAppointmentId,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        IAppointmentTypeRepository types,
        IOfficeScheduleRepository offices,
        CancellationToken ct
    )
    {
        var slots = await FindForAsync(
            tenantId,
            preparerUserId,
            bookable,
            startUtc.AddHours(-12),
            startUtc.AddHours(12),
            excludeAppointmentId,
            appointments,
            availability,
            types,
            offices,
            ct
        );
        if (slots.IsFailure)
            return false;
        foreach (var slot in slots.Value)
        {
            if (slot.StartUtc == startUtc)
                return true;
        }

        return false;
    }

    /// <summary>Los de un preparador, recortados a la antelación y al máximo de la política.</summary>
    private static async Task<Result<IReadOnlyList<TimeSlot>>> FindForAsync(
        Guid tenantId,
        Guid preparerUserId,
        Bookable bookable,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? excludeAppointmentId,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        IAppointmentTypeRepository types,
        IOfficeScheduleRepository offices,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var from = Max(fromUtc, now.AddMinutes(bookable.Policy.MinNoticeMinutes));
        var to = Min(toUtc, now.AddDays(bookable.Policy.MaxDaysAhead));
        if (to <= from)
            return Result.Success<IReadOnlyList<TimeSlot>>([]);

        var free = await GetAvailabilityHandler.Handle(
            new GetAvailabilityQuery(tenantId, preparerUserId, from, to, bookable.Type.Id)
            {
                BufferMinutes = bookable.Policy.BufferMinutes,
                ExcludeAppointmentId = excludeAppointmentId,
            },
            appointments,
            availability,
            types,
            offices,
            ct
        );
        if (free.IsFailure)
            return Result.Failure<IReadOnlyList<TimeSlot>>(free.Error);

        var windows = new List<TimeSlot>();
        foreach (var slot in free.Value)
            windows.Add(new TimeSlot(slot.StartUtc, slot.EndUtc));

        return Result.Success(
            BookingSlots.Plan(windows, bookable.Type.DefaultDuration, bookable.Policy.SlotStepMinutes)
        );
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
