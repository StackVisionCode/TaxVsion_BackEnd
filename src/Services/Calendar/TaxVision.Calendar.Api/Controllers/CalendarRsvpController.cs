using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.Appointments.Rsvp;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// La respuesta del invitado desde el correo, sin sesión: el token del enlace es la credencial.
///
/// <para>
/// Defensa: límite nativo por IP (no hay tenant ni usuario que particionar), el token en el cuerpo y
/// nunca en la URL, nada en caché, y la misma respuesta para un token inexistente o ya reemplazado.
/// Abrir el enlace no responde nada: los escáneres de correo siguen los GET y responderían solos.
/// </para>
/// </summary>
[ApiController]
[Route("calendar/rsvp")]
[AllowAnonymous]
[RateLimitExempt("Anonymous RSVP is protected by the native per-IP policy calendar-rsvp.")]
public sealed class CalendarRsvpController(IMessageBus bus) : ControllerBase
{
    public const string RateLimitPolicy = "calendar-rsvp";

    [HttpPost("view")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> View([FromBody] RsvpViewRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AppointmentRsvpView>>(
            new GetAppointmentRsvpQuery(request.Token ?? string.Empty),
            ct
        );
        return result.IsFailure ? NotFound() : Ok(result.Value);
    }

    [HttpPost("answer")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Answer([FromBody] RsvpAnswerRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AppointmentRsvpView>>(
            new RespondByRsvpCommand(request.Token ?? string.Empty, request.Response),
            ct
        );
        if (result.IsSuccess)
            return Ok(result.Value);

        // Inexistente y reemplazado dicen lo mismo: distinguirlos sería un oráculo de tokens.
        return result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal)
            ? NotFound()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
