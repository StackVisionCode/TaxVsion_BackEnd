using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.Types;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// El visitante pide un horario: queda retenido con un anfitrión y se le manda un código. El horario se toma
/// antes de mirar el email, así un horario ocupado responde igual para cualquier email. Un bot, el cupo por email
/// o quien ya tiene una demo reciben la misma respuesta que un pedido válido, sin efecto.
/// </summary>
public static class RequestPublicBookingHandler
{
    public static async Task<Result<PublicBookingRequestAccepted>> Handle(
        RequestPublicBookingCommand command,
        IBookingPageRepository pages,
        IPublicBookingRepository bookings,
        PublicBookingScheduler scheduler,
        PublicBookingNotices notices,
        IPublicBookingBotCheck bot,
        IRequesterKeyHasher hasher,
        IOptions<PublicBookingOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var page = await pages.FindActiveBySlugAsync(options.Value.EffectiveTenantIds, command.Slug, ct);
        var type = page is null ? null : await scheduler.TypeOfAsync(page, ct);
        if (page is null || type is null || !bot.IsReady)
            return Result.Failure<PublicBookingRequestAccepted>(PublicBookingErrors.NotFound);
        if (!command.PassedBotCheck)
            return Silent(options.Value, now);

        var data = PublicBookingFormReader.Read(page, command.Form, hasher);
        if (data.IsFailure)
            return Result.Failure<PublicBookingRequestAccepted>(data.Error);
        var key = command.IdempotencyKey?.Trim() ?? string.Empty;
        if (
            key.Length is > 0 and <= PublicBooking.MaxIdempotencyKeyLength
            && await bookings.FindByIdempotencyKeyAsync(page.TenantId, page.Id, key, ct) is { } retried
        )
            return await RetryAsync(retried, data.Value.RequesterKey, options.Value, unitOfWork, now, ct);

        var issued = await HoldAsync(
            page,
            type,
            data.Value,
            command.Form.SlotStartUtc,
            key,
            bookings,
            scheduler,
            options.Value,
            unitOfWork,
            now,
            ct
        );
        if (issued.IsFailure)
            return Result.Failure<PublicBookingRequestAccepted>(issued.Error);
        if (issued.Value.Repeat is { } repeated)
        {
            // Ya tiene una demo: la pantalla responde como a cualquiera y el dueño del email recibe el aviso.
            await bus.PublishAsync(await notices.AlreadyBookedAsync(page, repeated.Booking, repeated.Token, ct));
            return Silent(options.Value, now);
        }
        if (issued.Value.Issued is not { } held)
            return Silent(options.Value, now);

        if (held.Code is { } code)
            await bus.PublishAsync(notices.CodeRequested(page, held.Booking, code));
        return Result.Success(new PublicBookingRequestAccepted(held.RequestHandle, held.Booking.HoldExpiresAtUtc));
    }

    /// <summary>Lo que salió del pedido: una retención nueva, el aviso a quien ya tiene demo, o nada (silencio).</summary>
    private sealed record Held(PublicBookingIssued? Issued, (PublicBooking Booking, string Token)? Repeat)
    {
        public static Held Silent { get; } = new(null, null);
    }

    /// <summary>
    /// Toma el horario, revisa los cupos del visitante y guarda la retención. La pendiente anterior del mismo
    /// visitante se cierra recién cuando la nueva es válida. Quien ya tiene una demo confirmada no crea otra: se le
    /// rota el enlace (una vez por hora) para avisarle por correo.
    /// </summary>
    private static async Task<Result<Held>> HoldAsync(
        BookingPage page,
        AppointmentType type,
        PublicBookingRequestData data,
        DateTime slotStartUtc,
        string key,
        IPublicBookingRepository bookings,
        PublicBookingScheduler scheduler,
        PublicBookingOptions options,
        IUnitOfWork unitOfWork,
        DateTime now,
        CancellationToken ct
    )
    {
        var active = await bookings.FindActiveByRequesterAsync(page.TenantId, page.Id, data.RequesterKey, ct);
        var host = await scheduler.ClaimHostAsync(page, type, slotStartUtc, null, active?.Id, null, now, ct);
        if (host is null)
            return Result.Failure<Held>(PublicBookingErrors.SlotTaken);
        if (active?.Status == PublicBookingStatus.Confirmed)
        {
            var token = active.NoteRepeatRequest(now);
            if (token.IsFailure)
                return Result.Success(Held.Silent);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(new Held(null, (active, token.Value)));
        }
        var recent = await bookings.CountRequestsSinceAsync(page.TenantId, data.RequesterKey, now.AddHours(-1), ct);
        if (recent >= options.MaxRequestsPerEmailPerHour)
            return Result.Success(Held.Silent);

        var issued = PublicBooking.Request(
            page.TenantId,
            page.Id,
            data,
            slotStartUtc,
            (int)type.DefaultDuration.TotalMinutes,
            host.Value,
            key,
            page.RequireEmailVerification,
            options.HoldMinutes,
            now
        );
        if (issued.IsFailure)
            return Result.Failure<Held>(issued.Error);

        // El índice único no admite dos activas del mismo visitante: la vieja se cierra y se guarda primero.
        if (active is not null && active.ExpireReplaced(now).IsSuccess)
            await unitOfWork.SaveChangesAsync(ct);
        bookings.Add(issued.Value.Booking);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new Held(issued.Value, null));
    }

    /// <summary>El mismo envío otra vez: si sigue pendiente y es del mismo visitante, un manejo nuevo; si no, silencio.</summary>
    private static async Task<Result<PublicBookingRequestAccepted>> RetryAsync(
        PublicBooking retried,
        string requesterKey,
        PublicBookingOptions options,
        IUnitOfWork unitOfWork,
        DateTime now,
        CancellationToken ct
    )
    {
        if (retried.RequesterKey != requesterKey)
            return Silent(options, now);
        var handle = retried.ReissueRequestHandle(now);
        if (handle.IsFailure)
            return Silent(options, now);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new PublicBookingRequestAccepted(handle.Value, retried.HoldExpiresAtUtc));
    }

    /// <summary>La respuesta de "todo bien" sin nada detrás: el manejo no existe y no hay código.</summary>
    private static Result<PublicBookingRequestAccepted> Silent(PublicBookingOptions options, DateTime now) =>
        Result.Success(
            new PublicBookingRequestAccepted(OpaqueToken.Create().Value, now.AddMinutes(options.HoldMinutes))
        );
}
