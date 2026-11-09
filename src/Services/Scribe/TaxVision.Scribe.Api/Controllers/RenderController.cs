using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using Wolverine;

namespace TaxVision.Scribe.Api.Controllers;

/// <summary>
/// Endpoint HTTP de render por EventKey (Fase 7, plan §36) — arma el mismo <see
/// cref="RenderEmailQuery"/> que consume Notification, vía IMessageBus. El gRPC equivalente
/// (`TemplateRenderGrpcService`) existió en paralelo desde la Fase 7 pero nunca tuvo un caller
/// real (Notification siempre usó este endpoint HTTP) — retirado en la Fase 8 del hardening
/// (ver ADR-0003, README §36.2/36.7).
/// </summary>
[ApiController]
[Route("scribe/render")]
[Authorize]
[AllowActorTypes(ActorType.Service)]
public sealed class RenderController(IMessageBus bus) : ControllerBase
{
    public sealed record RenderHttpRequest(
        string EventKey,
        Guid? TenantId,
        string? Locale,
        IReadOnlyDictionary<string, object?> Variables,
        LogoScope LogoScope = LogoScope.System,
        TemplateChannel Channel = TemplateChannel.Email,
        // Quién lee el correo: Client usa el logo del portal de la oficina. Ausente = Staff.
        RenderAudience Audience = RenderAudience.Staff
    );

    [HttpPost]
    [HasPermission(ScribePermissions.Render)]
    [RateLimit("scribe.j.render")]
    [ProducesResponseType<RenderedContent>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Render([FromBody] RenderHttpRequest request, CancellationToken ct)
    {
        var query = new RenderEmailQuery(
            request.EventKey,
            request.TenantId,
            request.Locale,
            request.Variables,
            request.LogoScope,
            request.Channel,
            request.Audience
        );
        var result = await bus.InvokeAsync<Result<RenderedContent>>(query, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(ToRenderStatusCode(result.Error), result.Error);
    }

    // SCR-11: pasarse del tiempo depende de la carga, no del pedido; Notification reintenta los 5xx.
    private static int ToRenderStatusCode(Error error) =>
        error.Code == "EmailRenderer.Timeout" ? StatusCodes.Status503ServiceUnavailable : error.ToHttpStatusCode();
}
