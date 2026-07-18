using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Api.Authorization;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
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
public sealed class RenderController(IMessageBus bus) : ControllerBase
{
    public sealed record RenderHttpRequest(
        string EventKey,
        Guid? TenantId,
        string? Locale,
        IReadOnlyDictionary<string, object?> Variables,
        LogoScope LogoScope = LogoScope.System
    );

    [HttpPost]
    [HasPermission(ScribePermissions.Render)]
    [ProducesResponseType<RenderedContent>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Render([FromBody] RenderHttpRequest request, CancellationToken ct)
    {
        var query = new RenderEmailQuery(
            request.EventKey,
            request.TenantId,
            request.Locale,
            request.Variables,
            request.LogoScope
        );
        var result = await bus.InvokeAsync<Result<RenderedContent>>(query, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
