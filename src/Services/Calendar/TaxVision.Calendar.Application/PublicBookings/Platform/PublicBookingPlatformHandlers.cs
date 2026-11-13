using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings.Platform;

// El equipo de la plataforma atiende las reservas. La reserva y su cita cambian juntas: la cita de una
// página pública no se toca por la vía normal del CRM (ManagedByPublicPage).

/// <summary>Cancela la demo y su cita; el visitante recibe el aviso con el .ics de cancelación.</summary>
public static class StaffCancelPublicBookingHandler
{
    public static async Task<Result<PublicBookingAdminItem>> Handle(
        StaffCancelPublicBookingCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var booking = await bookings.GetByIdAsync(command.TenantId, command.BookingId, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking?.AppointmentId is not { } appointmentId || page is null)
            return Result.Failure<PublicBookingAdminItem>(PublicBookingErrors.NotFound);
        var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
        if (appointment.IsFailure)
            return Result.Failure<PublicBookingAdminItem>(PublicBookingErrors.NotFound);

        // La cita se revisa antes de tocar la reserva: Wolverine guarda aunque el handler devuelva un error.
        var canChange = appointment.Value.CanChangeFromPublicPage();
        if (canChange.IsFailure)
            return Result.Failure<PublicBookingAdminItem>(canChange.Error);
        var cancelled = booking.CancelByStaff(command.UserId, command.Reason, now);
        if (cancelled.IsFailure)
            return Result.Failure<PublicBookingAdminItem>(cancelled.Error);
        appointment.Value.CancelByPublicPageStaff(command.UserId, command.Reason, now);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PlatformViews.Booking(page, booking));
    }
}

/// <summary>Mueve la demo y su cita con las mismas reglas de horario que el visitante; el enlace de gestión rota.</summary>
public static class StaffReschedulePublicBookingHandler
{
    public static async Task<Result<PublicBookingAdminItem>> Handle(
        StaffReschedulePublicBookingCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        IAppointmentRepository appointments,
        PublicBookingScheduler scheduler,
        PublicBookingNotices notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var booking = await bookings.GetByIdAsync(command.TenantId, command.BookingId, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking?.AppointmentId is not { } appointmentId || page is null)
            return Result.Failure<PublicBookingAdminItem>(PublicBookingErrors.NotFound);
        var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
        if (appointment.IsFailure)
            return Result.Failure<PublicBookingAdminItem>(PublicBookingErrors.NotFound);

        var previous = booking.SlotStartUtc;
        var moved = await scheduler.MoveAsync(
            page,
            booking,
            appointment.Value,
            command.SlotStartUtc,
            now,
            ct,
            staffUserId: command.UserId,
            reason: command.Reason
        );
        if (moved.IsFailure)
            return Result.Failure<PublicBookingAdminItem>(moved.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            await notices.RescheduledAsync(
                page,
                booking,
                previous,
                PublicBookingActor.Staff,
                command.Reason,
                moved.Value,
                ct
            )
        );
        return Result.Success(PlatformViews.Booking(page, booking));
    }
}

/// <summary>Anota si la demo se hizo o el visitante no llegó (solo después de terminada).</summary>
public static class MarkPublicBookingOutcomeHandler
{
    public static async Task<Result<PublicBookingAdminItem>> Handle(
        MarkPublicBookingOutcomeCommand command,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var booking = await bookings.GetByIdAsync(command.TenantId, command.BookingId, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking is null || page is null)
            return Result.Failure<PublicBookingAdminItem>(PublicBookingErrors.NotFound);

        var marked = booking.MarkOutcome(command.Outcome, command.UserId, DateTime.UtcNow);
        if (marked.IsFailure)
            return Result.Failure<PublicBookingAdminItem>(marked.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PlatformViews.Booking(page, booking));
    }
}

/// <summary>Las reservas de una página por horario de la demo, filtradas y paginadas.</summary>
public static class ListPublicBookingsHandler
{
    public const int MaxRangeDays = 366;
    public const int MaxPageSize = 200;

    public static async Task<Result<PublicBookingListResponse>> Handle(
        ListPublicBookingsQuery query,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        CancellationToken ct
    )
    {
        if (!IsValidRange(query.FromUtc, query.ToUtc) || query.Page < 1 || query.PageSize is < 1 or > MaxPageSize)
            return Result.Failure<PublicBookingListResponse>(PublicBookingErrors.RangeInvalid);
        var page = await pages.GetByIdAsync(query.TenantId, query.PageId, ct);
        if (page is null)
            return Result.Failure<PublicBookingListResponse>(BookingPageErrors.NotFound);

        var filter = new PublicBookingFilter(
            query.TenantId,
            query.PageId,
            query.FromUtc,
            query.ToUtc,
            query.Status,
            string.IsNullOrWhiteSpace(query.HeardAbout) ? null : query.HeardAbout.Trim(),
            query.Source
        );
        var (items, total) = await bookings.ListAsync(filter, query.Page, query.PageSize, ct);
        var views = new List<PublicBookingAdminItem>(items.Count);
        foreach (var booking in items)
            views.Add(PlatformViews.Booking(page, booking));
        return Result.Success(new PublicBookingListResponse(views, total, query.Page, query.PageSize));
    }

    internal static bool IsValidRange(DateTime fromUtc, DateTime toUtc) =>
        toUtc > fromUtc && toUtc - fromUtc <= TimeSpan.FromDays(MaxRangeDays);
}

/// <summary>Cuántas demos del rango por estado, por "How did you hear about us?" y por origen.</summary>
public static class GetPublicBookingSummaryHandler
{
    public static async Task<Result<PublicBookingSummaryResponse>> Handle(
        GetPublicBookingSummaryQuery query,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        CancellationToken ct
    )
    {
        if (!ListPublicBookingsHandler.IsValidRange(query.FromUtc, query.ToUtc))
            return Result.Failure<PublicBookingSummaryResponse>(PublicBookingErrors.RangeInvalid);
        var page = await pages.GetByIdAsync(query.TenantId, query.PageId, ct);
        if (page is null)
            return Result.Failure<PublicBookingSummaryResponse>(BookingPageErrors.NotFound);

        var counts = await bookings.CountAsync(
            new PublicBookingFilter(query.TenantId, query.PageId, query.FromUtc, query.ToUtc),
            ct
        );
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in page.HeardAboutOptions)
            labels[option.Code] = option.LabelEn;

        var total = 0;
        foreach (var count in counts.ByStatus.Values)
            total += count;
        return Result.Success(
            new PublicBookingSummaryResponse(
                total,
                Rows(counts.ByStatus, key => key.ToString(), _ => null),
                Rows(counts.ByHeardAbout, key => key, key => labels.GetValueOrDefault(key)),
                Rows(counts.BySource, key => key.ToString(), _ => null),
                Rows(counts.ByUtmSource, key => key.Length == 0 ? "(none)" : key, _ => null)
            )
        );
    }

    /// <summary>De mayor a menor; a igual cantidad, por clave (orden estable para el panel).</summary>
    private static List<PublicBookingCount> Rows<TKey>(
        IReadOnlyDictionary<TKey, int> source,
        Func<TKey, string> key,
        Func<string, string?> label
    )
        where TKey : notnull
    {
        var rows = new List<PublicBookingCount>(source.Count);
        foreach (var (rawKey, count) in source)
        {
            var name = key(rawKey);
            rows.Add(new PublicBookingCount(name, label(name), count));
        }
        rows.Sort((a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Key, b.Key));
        return rows;
    }
}
