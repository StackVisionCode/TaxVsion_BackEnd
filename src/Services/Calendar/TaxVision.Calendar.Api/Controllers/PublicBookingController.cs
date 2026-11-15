using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// "Book a live demo" del landing, sin sesión. Los secretos (manejo de la solicitud y enlace de gestión) van
/// en el cuerpo, nunca en la URL; nada se guarda en caché salvo los horarios (30 s). Un bot, un cupo o un
/// secreto inválido reciben la misma respuesta que un caso válido equivalente.
/// </summary>
[ApiController]
[Route("calendar/public")]
[AllowAnonymous]
[RateLimitExempt("Anonymous public booking is protected by the native per-IP policies calendar-public-*.")]
public sealed class PublicBookingController(IMessageBus bus, IPublicBookingBotCheck botCheck, ICalendarMetrics metrics)
    : ControllerBase
{
    public const string ReadPolicy = "calendar-public-read";
    public const string BookingPolicy = "calendar-public-booking";
    public const string VerifyPolicy = "calendar-public-verify";
    public const string ManagePolicy = "calendar-public-manage";
    private const string IdempotencyHeader = "Idempotency-Key";

    [HttpGet("booking-pages/{slug}")]
    [AllowAnonymous]
    [EnableRateLimiting(ReadPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetPage(string slug, [FromQuery] string? lang, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PublicBookingPageResponse>>(
            new GetPublicBookingPageQuery(slug, lang),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpGet("booking-pages/{slug}/slots")]
    [AllowAnonymous]
    [EnableRateLimiting(ReadPolicy)]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetSlots(
        string slug,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct
    )
    {
        var query = new GetPublicBookingSlotsQuery(slug, AsUtc(from), AsUtc(to));
        var result = await bus.InvokeAsync<Result<PublicBookingSlotsResponse>>(query, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    [HttpPost("booking-pages/{slug}/bookings")]
    [AllowAnonymous]
    [EnableRateLimiting(BookingPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Request(
        string slug,
        [FromBody] PublicBookingCreateRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        CancellationToken ct
    )
    {
        // Turnstile acá y no en el handler: la llamada a Cloudflare no retiene una transacción abierta.
        var passed = await botCheck.PassesAsync(
            request.BotToken,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct
        );
        var command = new RequestPublicBookingCommand(
            slug,
            idempotencyKey,
            passed,
            new PublicBookingForm(
                AsUtc(request.SlotStartUtc),
                request.Name,
                request.Email,
                request.Company,
                request.OfficeSize,
                request.Phone,
                request.Interest,
                request.HeardAboutCode,
                request.HeardAboutDetail,
                request.EntrySource,
                request.UtmSource,
                request.UtmMedium,
                request.UtmCampaign,
                request.ReferrerDomain,
                request.Language,
                request.TimeZone,
                request.MarketingOptIn,
                request.ConsentVersion
            )
        );
        var result = await bus.InvokeAsync<Result<PublicBookingRequestAccepted>>(command, ct);
        return Respond("request", result, value => Accepted(value));
    }

    [HttpPost("bookings/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(VerifyPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Verify([FromBody] PublicBookingVerifyRequest request, CancellationToken ct)
    {
        var command = new VerifyPublicBookingCommand(request.RequestHandle, request.Code);
        var result = await bus.InvokeAsync<Result<PublicBookingConfirmation>>(command, ct);
        return Respond("verify", result, value => Ok(value));
    }

    [HttpPost("bookings/choose-slot")]
    [AllowAnonymous]
    [EnableRateLimiting(BookingPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ChooseSlot([FromBody] PublicBookingChooseSlotRequest request, CancellationToken ct)
    {
        var command = new ChoosePublicBookingSlotCommand(request.RequestHandle, AsUtc(request.SlotStartUtc));
        var result = await bus.InvokeAsync<Result>(command, ct);
        return Respond("choose_slot", result, Accepted);
    }

    [HttpPost("bookings/resend")]
    [AllowAnonymous]
    [EnableRateLimiting(BookingPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Resend([FromBody] PublicBookingResendRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new ResendPublicBookingCodeCommand(request.RequestHandle), ct);
        return Respond("resend", result, Accepted);
    }

    [HttpPost("bookings/manage/view")]
    [AllowAnonymous]
    [EnableRateLimiting(ManagePolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> View([FromBody] PublicBookingManageRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PublicBookingManageView>>(
            new ViewPublicBookingQuery(request.Token),
            ct
        );
        return Respond("manage_view", result, value => Ok(value));
    }

    [HttpPost("bookings/manage/reschedule")]
    [AllowAnonymous]
    [EnableRateLimiting(ManagePolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Reschedule([FromBody] PublicBookingRescheduleRequest request, CancellationToken ct)
    {
        var command = new ReschedulePublicBookingCommand(request.Token, AsUtc(request.SlotStartUtc));
        var result = await bus.InvokeAsync<Result<PublicBookingRescheduled>>(command, ct);
        return Respond("manage_reschedule", result, value => Ok(value));
    }

    [HttpPost("bookings/manage/cancel")]
    [AllowAnonymous]
    [EnableRateLimiting(ManagePolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Cancel([FromBody] PublicBookingCancelRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new CancelPublicBookingCommand(request.Token, request.Reason), ct);
        return Respond("manage_cancel", result, Ok);
    }

    private IActionResult Respond<T>(string step, Result<T> result, Func<T, IActionResult> success)
    {
        metrics.RecordPublicBooking(step, result.IsSuccess ? "ok" : result.Error.Code);
        return result.IsSuccess ? success(result.Value) : Problem(result.Error);
    }

    private IActionResult Respond(string step, Result result, Func<IActionResult> success)
    {
        metrics.RecordPublicBooking(step, result.IsSuccess ? "ok" : result.Error.Code);
        return result.IsSuccess ? success() : Problem(result.Error);
    }

    /// <summary>Un código estable y un mensaje para el visitante; nunca detalles internos.</summary>
    private ObjectResult Problem(Error error) => StatusCode(StatusOf(error), new { error.Code, error.Message });

    public static int StatusOf(Error error)
    {
        if (error == PublicBookingErrors.NotFound)
            return StatusCodes.Status404NotFound;
        if (
            error == PublicBookingErrors.SlotTaken
            || error == PublicBookingErrors.AlreadyHappened
            || error == PublicBookingErrors.NotConfirmed
        )
            return StatusCodes.Status409Conflict;
        if (
            error == PublicBookingErrors.HoldExpired
            || error == PublicBookingErrors.TooManyAttempts
            || error == PublicBookingErrors.NotPending
        )
            return StatusCodes.Status410Gone;
        return error == PublicBookingErrors.TooManyCodes
            ? StatusCodes.Status429TooManyRequests
            : StatusCodes.Status400BadRequest;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
