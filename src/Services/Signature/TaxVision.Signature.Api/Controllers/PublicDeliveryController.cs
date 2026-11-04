using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Signature.Application.Delivery;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Entrega de documentos firmados (página <c>/d/{token}</c> del front). La autoriza el secreto del enlace,
/// sin sesión; no es un share de CloudStorage. Cada descarga pide en ese momento una URL firmada de pocos
/// minutos. Bajo el subdominio de otra oficina responde como si el enlace no existiera (anti-phishing).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("signature/delivery")]
[EnableRateLimiting("public-signature")]
public sealed class PublicDeliveryController(IMessageBus bus) : ControllerBase
{
    private const string PublicExemptReason =
        "Endpoint anonimo (sin JWT): TieredRateLimitEvaluator solo particiona por Tenant/User. La "
        + "proteccion real la da el limiter nativo (IP+ruta), mismo criterio que PublicSignatureController.";

    // Mismo header que fija TaxVision.Gateway.Middleware.TenantHostGuardMiddleware.
    private const string ResolvedTenantHeader = "X-Resolved-Tenant";

    // ---------- GET /signature/delivery/{token} ----------
    [HttpGet("{token}")]
    [RateLimitExempt(PublicExemptReason)]
    [ProducesResponseType<PublicDeliveryView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> View([FromRoute] string token, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PublicDeliveryView>>(
            new GetPublicDeliveryQuery(token, HostTenantId()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/delivery/{token}/files/{itemId}/download-url ----------
    [HttpPost("{token}/files/{itemId:guid}/download-url")]
    [RateLimitExempt(PublicExemptReason)]
    [EnableRateLimiting("public-signature-document")]
    [ProducesResponseType<DeliveryFileLink>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadUrl(
        [FromRoute] string token,
        [FromRoute] Guid itemId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<DeliveryFileLink>>(
            new OpenDeliveryFileCommand(token, itemId, HostTenantId()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- POST /signature/delivery/{token}/reissue ----------
    // Enlace vencido: manda uno nuevo al email ORIGINAL de la entrega (quien tiene el enlace no elige destino).
    [HttpPost("{token}/reissue")]
    [RateLimitExempt(PublicExemptReason)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reissue([FromRoute] string token, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new RequestNewDeliveryLinkCommand(token, HostTenantId()), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // Sin tenant resuelto por Host (host de sistema, localhost/dev) no se valida.
    private Guid? HostTenantId() =>
        Guid.TryParse(Request.Headers[ResolvedTenantHeader].ToString(), out var tenantId) ? tenantId : null;
}
