using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Signature.Application.Audit;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Endpoints públicos para el firmante externo autorizado sólo por el token firmado
/// que recibió por correo/SMS. El token codifica <c>TenantId + RequestId + SignerId +
/// RevocationEpoch + exp</c>; el resolver central verifica firma, expiración y epoch.
///
/// <para>
/// Cada endpoint tiene su método privado por fase (extracción de metadata del request,
/// invocación del bus, mapeo de errores) — sin acumular responsabilidades.
/// </para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("signature/public")]
[EnableRateLimiting("public-signature")]
public sealed class PublicSignatureController(IMessageBus bus) : ControllerBase
{
    // ---------- GET /signature/public/{token} ----------
    [HttpGet("{token}")]
    [ProducesResponseType<PublicSignerView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> View([FromRoute] string token, CancellationToken ct)
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result<PublicSignerView>>(new ViewPublicSignerCommand(token, ip, ua), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/public/{token}/consent ----------
    [HttpPost("{token}/consent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AcceptConsent([FromRoute] string token, CancellationToken ct)
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result>(new AcceptConsentCommand(token, ip, ua), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/public/{token}/sign ----------
    [HttpPost("{token}/sign")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sign(
        [FromRoute] string token,
        [FromBody] SubmitSignatureBody body,
        CancellationToken ct
    )
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result>(
            new SubmitSignatureCommand(token, body.Method, body.TypedName, body.SignatureImageFileId, ip, ua),
            ct
        );
        return MapResult(result);
    }

    // ---------- POST /signature/public/{token}/verify-pin ----------
    [HttpPost("{token}/verify-pin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyPin(
        [FromRoute] string token,
        [FromBody] VerifyPinBody body,
        CancellationToken ct
    )
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result>(new VerifyPinCommand(token, body.Pin, ip, ua), ct);
        return MapResult(result);
    }

    // ---------- POST /signature/public/{token}/challenge ----------
    // Solicita la emisión de un reto para un canal concreto (SMS OTP, Email OTP, etc.).
    // Signature genera el código, lo guarda hasheado y publica un evento que consume el
    // microservicio entregador correspondiente. Signature no conoce Twilio/SendGrid/etc.
    //
    // Casos que este endpoint cubre naturalmente por diseño:
    //  - Emitir un OTP inicial (llamar con Method = SmsOtp/EmailOtp/etc).
    //  - RESEND del mismo método: llamar de nuevo → aggregate invalida el anterior y emite
    //    uno nuevo (respetando cooldown de 30s para evitar spam).
    //  - SWITCH-CHANNEL: llamar con un Method distinto → invalida el anterior de ese
    //    método y emite en el nuevo canal, sin cooldown (canal distinto).
    [HttpPost("{token}/challenge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IssueChallenge(
        [FromRoute] string token,
        [FromBody] IssueChallengeBody body,
        CancellationToken ct
    )
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result>(
            new IssueVerificationChallengeCommand(token, body.Method, ip, ua),
            ct
        );
        return MapResult(result);
    }

    // ---------- POST /signature/public/{token}/verify-challenge ----------
    [HttpPost("{token}/verify-challenge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyChallenge(
        [FromRoute] string token,
        [FromBody] VerifyChallengeBody body,
        CancellationToken ct
    )
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result>(
            new VerifyChallengeCommand(token, body.Method, body.Answer, ip, ua),
            ct
        );
        return MapResult(result);
    }

    // ---------- GET /signature/public/{token}/verify-audit ----------
    // Verifica públicamente que la cadena de audit de la solicitud no ha sido alterada.
    // Autorizado por el mismo token que autoriza al firmante; no expone secretos ni
    // permite mutar nada — solo devuelve el veredicto y el material verificable.
    [HttpGet("{token}/verify-audit")]
    [ProducesResponseType<AuditChainVerificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyAudit([FromRoute] string token, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AuditChainVerificationResponse>>(
            new VerifyAuditChainPublicQuery(token),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/public/{token}/reject ----------
    [HttpPost("{token}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reject(
        [FromRoute] string token,
        [FromBody] RejectSignatureBody body,
        CancellationToken ct
    )
    {
        var (ip, ua) = ExtractClientContext();
        var result = await bus.InvokeAsync<Result>(new RejectSignatureCommand(token, body.Reason, ip, ua), ct);
        return MapResult(result);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private (string? Ip, string? UserAgent) ExtractClientContext()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();
        return (ip, string.IsNullOrWhiteSpace(ua) ? null : ua);
    }

    private IActionResult MapResult(Result<PublicSignerView> result) =>
        result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);

    private IActionResult MapResult(Result result) =>
        result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}

public sealed record SubmitSignatureBody(SignatureCaptureMethod Method, string? TypedName, Guid? SignatureImageFileId);

public sealed record RejectSignatureBody(string? Reason);

public sealed record VerifyPinBody(string Pin);

public sealed record IssueChallengeBody(SignerVerificationMethod Method);

public sealed record VerifyChallengeBody(SignerVerificationMethod Method, string Answer);
