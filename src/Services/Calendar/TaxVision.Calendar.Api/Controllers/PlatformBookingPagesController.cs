using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.PublicBookings.Platform;
using TaxVision.Calendar.Domain.PublicBookings;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// Las páginas públicas de reservas y sus demos, solo para la plataforma. La UI irá en el futuro admin panel;
/// mientras tanto se usa con la colección de Postman. El tenant es el del token: no viaja en la petición.
/// </summary>
[ApiController]
[Route("calendar/platform")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlatformBookingPagesController(IMessageBus bus) : ControllerBase
{
    private const string ReadLimit = "calendar.f.read";
    private const string WriteLimit = "calendar.g.update";

    [HttpGet("booking-pages")]
    [RateLimit(ReadLimit)]
    public Task<IActionResult> List(CancellationToken ct) =>
        Run<IReadOnlyList<BookingPageSummary>>((tenant, _) => new ListBookingPagesQuery(tenant), ct);

    [HttpGet("booking-pages/{id:guid}")]
    [RateLimit(ReadLimit)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Run<BookingPageAdminView>((tenant, _) => new GetBookingPageQuery(tenant, id), ct);

    [HttpPost("booking-pages")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Create([FromBody] BookingPageRequest request, CancellationToken ct) =>
        Run<BookingPageAdminView>(
            (tenant, user) =>
                new CreateBookingPageCommand(
                    tenant,
                    user,
                    request.Slug,
                    request.TitleEn,
                    request.TitleEs,
                    request.DescriptionEn,
                    request.DescriptionEs,
                    request.AppointmentTypeId
                ),
            ct
        );

    [HttpPut("booking-pages/{id:guid}")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Update(Guid id, [FromBody] BookingPageRequest request, CancellationToken ct) =>
        Run<BookingPageAdminView>(
            (tenant, user) =>
                new UpdateBookingPageCommand(
                    tenant,
                    user,
                    id,
                    request.Slug,
                    request.TitleEn,
                    request.TitleEs,
                    request.DescriptionEn,
                    request.DescriptionEs,
                    request.AppointmentTypeId
                ),
            ct
        );

    [HttpPut("booking-pages/{id:guid}/rules")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Rules(Guid id, [FromBody] BookingPageRulesRequest request, CancellationToken ct) =>
        Run<BookingPageAdminView>(
            (tenant, user) =>
                new ChangeBookingPageRulesCommand(
                    tenant,
                    user,
                    id,
                    request.MinNoticeMinutes,
                    request.MaxDaysAhead,
                    request.BufferMinutes,
                    request.SlotStepMinutes,
                    request.RequireEmailVerification,
                    request.ReminderHoursBefore,
                    request.IcsAlarmMinutes ?? []
                ),
            ct
        );

    [HttpPut("booking-pages/{id:guid}/fields")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Fields(Guid id, [FromBody] BookingPageFieldsRequest request, CancellationToken ct) =>
        Run<BookingPageAdminView>(
            (tenant, user) =>
                new ConfigureBookingPageFieldsCommand(
                    tenant,
                    user,
                    id,
                    request.PhoneField,
                    request.OfficeSizeField,
                    request.InterestField
                ),
            ct
        );

    [HttpPut("booking-pages/{id:guid}/consent")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Consent(Guid id, [FromBody] BookingPageConsentRequest request, CancellationToken ct) =>
        Run<BookingPageAdminView>(
            (tenant, user) =>
                new ChangeBookingPageConsentCommand(
                    tenant,
                    user,
                    id,
                    request.PrivacyNoticeEn,
                    request.PrivacyNoticeEs,
                    request.MarketingConsentEn,
                    request.MarketingConsentEs,
                    request.ConsentVersion
                ),
            ct
        );

    [HttpPut("booking-pages/{id:guid}/hosts")]
    [RateLimit(WriteLimit)]
    public async Task<IActionResult> Hosts(Guid id, [FromBody] BookingPageHostsRequest request, CancellationToken ct)
    {
        // Auth se consulta en un paso aparte y sin base: la llamada HTTP no retiene la transacción del guardado.
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();
        var hosts = request.Hosts ?? [];
        var userIds = new List<Guid>(hosts.Count);
        foreach (var host in hosts)
            userIds.Add(host.UserId);
        var verified = await bus.InvokeAsync<Result>(new VerifyPlatformHostsQuery(tenantId, userIds), ct);
        if (verified.IsFailure)
            return StatusCode(StatusOf(verified.Error), new { verified.Error.Code, verified.Error.Message });

        return await Run<BookingPageAdminView>(
            (tenant, user) => new SetBookingPageHostsCommand(tenant, user, id, hosts),
            ct
        );
    }

    [HttpPut("booking-pages/{id:guid}/heard-about-options")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> HeardAboutOptions(
        Guid id,
        [FromBody] BookingPageHeardAboutOptionsRequest request,
        CancellationToken ct
    ) =>
        Run<BookingPageAdminView>(
            (tenant, user) => new SetBookingPageHeardAboutOptionsCommand(tenant, user, id, request.Options ?? []),
            ct
        );

    [HttpPost("booking-pages/{id:guid}/activate")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        Run<BookingPageAdminView>((tenant, user) => new ActivateBookingPageCommand(tenant, user, id), ct);

    [HttpPost("booking-pages/{id:guid}/deactivate")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        Run<BookingPageAdminView>((tenant, user) => new DeactivateBookingPageCommand(tenant, user, id), ct);

    [HttpGet("booking-pages/{id:guid}/bookings")]
    [RateLimit(ReadLimit)]
    public Task<IActionResult> Bookings(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] PublicBookingStatus? status,
        [FromQuery] string? heardAbout,
        [FromQuery] BookingEntrySource? source,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50
    ) =>
        Run<PublicBookingListResponse>(
            (tenant, _) =>
                new ListPublicBookingsQuery(
                    tenant,
                    id,
                    AsUtc(from),
                    AsUtc(to),
                    status,
                    heardAbout,
                    source,
                    page,
                    pageSize
                ),
            ct
        );

    [HttpGet("booking-pages/{id:guid}/bookings/summary")]
    [RateLimit(ReadLimit)]
    public Task<IActionResult> Summary(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct
    ) =>
        Run<PublicBookingSummaryResponse>(
            (tenant, _) => new GetPublicBookingSummaryQuery(tenant, id, AsUtc(from), AsUtc(to)),
            ct
        );

    [HttpPost("bookings/{bookingId:guid}/cancel")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> CancelBooking(
        Guid bookingId,
        [FromBody] StaffCancelPublicBookingRequest request,
        CancellationToken ct
    ) =>
        Run<PublicBookingAdminItem>(
            (tenant, user) => new StaffCancelPublicBookingCommand(tenant, user, bookingId, request.Reason),
            ct
        );

    [HttpPost("bookings/{bookingId:guid}/reschedule")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> RescheduleBooking(
        Guid bookingId,
        [FromBody] StaffReschedulePublicBookingRequest request,
        CancellationToken ct
    ) =>
        Run<PublicBookingAdminItem>(
            (tenant, user) =>
                new StaffReschedulePublicBookingCommand(
                    tenant,
                    user,
                    bookingId,
                    AsUtc(request.SlotStartUtc),
                    request.Reason
                ),
            ct
        );

    [HttpPost("bookings/{bookingId:guid}/outcome")]
    [RateLimit(WriteLimit)]
    public Task<IActionResult> Outcome(
        Guid bookingId,
        [FromBody] PublicBookingOutcomeRequest request,
        CancellationToken ct
    ) =>
        Run<PublicBookingAdminItem>(
            (tenant, user) => new MarkPublicBookingOutcomeCommand(tenant, user, bookingId, request.Outcome),
            ct
        );

    /// <summary>Tenant y usuario del token → mensaje → respuesta con código de error estable.</summary>
    private async Task<IActionResult> Run<T>(Func<Guid, Guid, object> message, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<T>>(message(tenantId, userId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(StatusOf(result.Error), new { result.Error.Code, result.Error.Message });
    }

    public static int StatusOf(Error error)
    {
        if (error == BookingPageErrors.NotFound || error == PublicBookingErrors.NotFound)
            return StatusCodes.Status404NotFound;
        if (
            error == BookingPageErrors.SlugTaken
            || error == PublicBookingErrors.SlotTaken
            || error == PublicBookingErrors.AlreadyHappened
            || error == PublicBookingErrors.NotConfirmed
            || error == PublicBookingErrors.NotFinishedYet
        )
            return StatusCodes.Status409Conflict;
        return error == BookingPageErrors.TenantNotAllowed
            ? StatusCodes.Status403Forbidden
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
