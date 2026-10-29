using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.Public.ClientForms;

[ApiController]
[ClientFormsEndpoint]
[Route("tasks/public/forms")]
[AllowAnonymous]
[SkipClientFormsAccess]
[RateLimitExempt("Anonymous public form access is protected by native IP and route policies.")]
public sealed class PublicFormsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("access/{token}")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> GetAccess(string token, CancellationToken ct)
    {
        var host = ResolveSourceHost();
        if (host is null)
            return NotFound();
        var result = await bus.InvokeAsync<Result<PublicDirectedFormAccessResponse>>(
            new GetPublicDirectedFormAccessQuery(token, host, DateTime.UtcNow),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpPost("access/{token}/challenge")]
    [EnableRateLimiting("public-forms-challenge")]
    [ProducesResponseType(typeof(PublicFormChallengeAcceptedResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Challenge(string token, CancellationToken ct)
    {
        var host = ResolveSourceHost();
        if (host is not null)
        {
            await bus.InvokeAsync<Result>(
                new RequestPublicDirectedFormChallengeCommand(token, host, DateTime.UtcNow),
                ct
            );
        }
        return Accepted(PublicFormChallengeAcceptedResponse.Instance);
    }

    [HttpPost("access/{token}/verify")]
    [EnableRateLimiting("public-forms-challenge")]
    public async Task<IActionResult> Verify(string token, VerifyPublicFormAccessRequest request, CancellationToken ct)
    {
        var host = ResolveSourceHost();
        if (host is null)
            return BadRequest(PublicDirectedFormAccessErrors.VerificationFailed);
        var result = await bus.InvokeAsync<Result<PublicFormSessionResponse>>(
            new VerifyPublicDirectedFormChallengeCommand(
                token,
                host,
                request.Code,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent(),
                DateTime.UtcNow,
                request.Language
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : BadRequest(PublicDirectedFormAccessErrors.VerificationFailed);
    }

    [HttpGet("intake/{token}")]
    [EnableRateLimiting("public-forms")]
    public async Task<IActionResult> GetIntake(string token, CancellationToken ct)
    {
        var host = ResolveSourceHost();
        if (host is null)
            return NotFound();
        var result = await bus.InvokeAsync<Result<PublicIntakeFormAccessResponse>>(
            new GetPublicIntakeFormAccessQuery(token, host, DateTime.UtcNow),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpPost("intake/{token}/challenge")]
    [EnableRateLimiting("public-forms-challenge")]
    [ProducesResponseType(typeof(PublicFormChallengeAcceptedResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ChallengeIntake(
        string token,
        RequestPublicIntakeChallengeRequest request,
        CancellationToken ct
    )
    {
        var host = ResolveSourceHost();
        if (host is not null)
        {
            await bus.InvokeAsync<Result>(
                new RequestPublicIntakeFormChallengeCommand(
                    token,
                    host,
                    request.Email,
                    request.CaptchaToken,
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    DateTime.UtcNow,
                    request.Language
                ),
                ct
            );
        }
        return Accepted(PublicFormChallengeAcceptedResponse.Instance);
    }

    [HttpPost("intake/{token}/verify")]
    [EnableRateLimiting("public-forms-challenge")]
    public async Task<IActionResult> VerifyIntake(
        string token,
        VerifyPublicIntakeChallengeRequest request,
        CancellationToken ct
    )
    {
        var host = ResolveSourceHost();
        if (host is null)
            return BadRequest(PublicIntakeFormErrors.VerificationFailed);
        var result = await bus.InvokeAsync<Result<PublicIntakeFormSessionResponse>>(
            new VerifyPublicIntakeFormChallengeCommand(
                token,
                host,
                request.Email,
                request.Code,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent(),
                DateTime.UtcNow,
                request.Language
            ),
            ct
        );
        if (result.IsSuccess)
            return Ok(result.Value);
        // Solo tras un código válido: la misma persona ya envió este enlace.
        return result.Error.Code == PublicIntakeFormErrors.AlreadySubmitted.Code
            ? Conflict(PublicIntakeFormErrors.AlreadySubmitted)
            : BadRequest(PublicIntakeFormErrors.VerificationFailed);
    }

    private string? ResolveSourceHost() => PublicFormRequestHost.Resolve(Request);

    // La sesión guarda como mucho 512 caracteres: se recorta en vez de rechazar la verificación.
    private string UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        return value.Length <= 512 ? value : value[..512];
    }
}
