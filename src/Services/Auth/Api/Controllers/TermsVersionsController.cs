using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    public sealed class PublishTermsVersionRequest
    {
        public TermsKind Kind { get; init; }
        public string Version { get; init; } = default!;
        public string Locale { get; init; } = default!;
        public DateTime? EffectiveUntilUtc { get; init; }
        public IFormFile File { get; init; } = default!;
    }

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

    /// <summary>Auditoría (gap MinIO/legal-docs) — mediador público del documento real; el
    /// frontend de onboarding lo consume para renderizar el texto de ToS/Privacy inline antes de
    /// que exista cualquier tenant. Ver GetTermsContentHandler para el razonamiento del capability
    /// opaco (Id de TermsVersion sin autenticación adicional).</summary>
    [HttpGet("{termsVersionId:guid}/content")]
    [AllowAnonymous]
    [EnableRateLimiting("terms-content-download")]
    public async Task<IActionResult> GetContent(Guid termsVersionId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<string>>(new GetTermsContentQuery(termsVersionId), ct);
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return Content(result.Value, "text/html; charset=utf-8");
    }

    [HttpPost("publish")]
    [Authorize]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<TermsVersionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Publish([FromForm] PublishTermsVersionRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        if (request.File is null || request.File.Length == 0)
            return BadRequest(new Error("Onboarding.TermsContentSizeInvalid", "A non-empty file is required."));

        using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream, ct);

        var result = await bus.InvokeAsync<Result<TermsVersionResponse>>(
            new PublishTermsVersionCommand(
                request.Kind,
                request.Version,
                stream.ToArray(),
                request.File.FileName,
                request.File.ContentType,
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
