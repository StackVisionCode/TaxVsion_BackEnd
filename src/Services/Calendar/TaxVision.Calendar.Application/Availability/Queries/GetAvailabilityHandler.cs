using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Calendar.Application.Availability.Queries;

public sealed record GetAvailabilityQuery(Guid TenantId, Guid UserId, DateTime FromUtc, DateTime ToUtc, Guid? TypeId);

public sealed record FreeSlotResponse(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Los huecos libres de una persona. Devuelve intervalos y <b>nunca</b> títulos: quien pregunta por la
/// agenda de un compañero no tiene por qué enterarse de con quién se reúne.
/// </summary>
public static class GetAvailabilityHandler
{
    public static async Task<Result<IReadOnlyList<FreeSlotResponse>>> Handle(
        GetAvailabilityQuery query,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        IAppointmentTypeRepository types,
        CancellationToken ct
    )
    {
        if (query.ToUtc <= query.FromUtc)
            return Result.Failure<IReadOnlyList<FreeSlotResponse>>(RecurrenceErrors.RangeInverted);

        var minimum = await MinimumSlotAsync(query, types, ct);
        if (minimum.IsFailure)
            return Result.Failure<IReadOnlyList<FreeSlotResponse>>(minimum.Error);

        var rules = await availability.ListRulesAsync(query.TenantId, query.UserId, ct);
        var blocks = await availability.ListBlocksAsync(query.TenantId, query.UserId, query.FromUtc, query.ToUtc, ct);
        var busy = await BusyAsync(query, appointments, ct);

        var windows = WorkingWindows.Build(rules, query.FromUtc, query.ToUtc);
        var free = ConflictDetector.FreeSlots(query.FromUtc, query.ToUtc, windows, busy, blocks, minimum.Value);

        var response = new List<FreeSlotResponse>();
        foreach (var slot in free)
            response.Add(new FreeSlotResponse(slot.StartUtc, slot.EndUtc));

        return Result.Success<IReadOnlyList<FreeSlotResponse>>(response);
    }

    private static async Task<Result<TimeSpan>> MinimumSlotAsync(
        GetAvailabilityQuery query,
        IAppointmentTypeRepository types,
        CancellationToken ct
    )
    {
        if (query.TypeId is not { } typeId)
            return Result.Success(TimeSpan.FromMinutes(15));

        var type = await types.GetByIdAsync(query.TenantId, typeId, ct);
        return type.IsFailure ? Result.Failure<TimeSpan>(type.Error) : Result.Success(type.Value.DefaultDuration);
    }

    private static async Task<List<Occurrence>> BusyAsync(
        GetAvailabilityQuery query,
        IAppointmentRepository appointments,
        CancellationToken ct
    )
    {
        var candidates = await appointments.ListForRangeAsync(query.TenantId, query.FromUtc, query.ToUtc, ct);
        var busy = new List<Occurrence>();

        foreach (var appointment in candidates)
        {
            if (appointment.Status == AppointmentStatus.Cancelled || !Involves(appointment, query.UserId))
                continue;

            var expanded = OccurrenceExpander.Expand(appointment, query.FromUtc, query.ToUtc);
            if (expanded.IsSuccess)
                busy.AddRange(expanded.Value);
        }

        return busy;
    }

    private static bool Involves(Appointment appointment, Guid userId)
    {
        if (appointment.OrganizerUserId == userId)
            return true;

        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId == userId)
                return true;
        }

        return false;
    }
}
