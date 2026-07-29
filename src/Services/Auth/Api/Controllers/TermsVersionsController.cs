using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Onboarding.TermsVersions.Commands;
using TaxVision.Auth.Application.Onboarding.TermsVersions.Queries;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow Fase 6 — versiones publicadas de documentos legales (ToS/Privacy Policy), recurso de plataforma sin tenant.</summary>
[ApiController]
[Route("auth/onboarding/terms")]
public sealed class TermsVersionsController(IMessageBus bus) : ControllerBase
{
    public sealed record PublishTermsVersionRequest(
        TermsKind Kind,
        string Version,
        string ContentUri,
        string Locale,
        DateTime? EffectiveUntilUtc = null
    );

    [HttpGet("current")]
    [AllowAnonymous]
    [ProducesResponseType<TermsVersionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(
        [FromQuery] TermsKind kind,
        [FromQuery] string locale,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<TermsVersionResponse>>(
            new GetCurrentTermsVersionQuery(kind, locale),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("publish")]
    [Authorize]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [ProducesResponseType<TermsVersionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Publish(PublishTermsVersionRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TermsVersionResponse>>(
            new PublishTermsVersionCommand(
                request.Kind,
                request.Version,
                request.ContentUri,
                request.Locale,
                userId,
                request.EffectiveUntilUtc
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }
}
