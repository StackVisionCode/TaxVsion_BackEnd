using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Referrals.Application.Codes.ValidateReferralCode;
using Wolverine;

namespace TaxVision.Growth.Api.Controllers;

/// <summary>
/// Endpoints PÚBLICOS de referidos (sin sesión): el landing valida el código del enlace antes del alta. Responden lo
/// mínimo (un booleano) y limitan por IP; nunca devuelven dueño, programa ni beneficios.
/// </summary>
[ApiController]
[Route("growth/public/referrals")]
[AllowAnonymous]
public sealed class PublicReferralsController(IMessageBus bus) : ControllerBase
{
    /// <summary>¿El código de referido sirve hoy? <c>{ isActive }</c>. Un código reemplazado o vencido da <c>false</c>.</summary>
    [HttpGet("codes/{code}/validate")]
    [RateLimit("growth.a.referral_validate_by_ip")]
    [ProducesResponseType<ValidateReferralCodeResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate(string code, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ValidateReferralCodeResult>>(new ValidateReferralCodeQuery(code), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
