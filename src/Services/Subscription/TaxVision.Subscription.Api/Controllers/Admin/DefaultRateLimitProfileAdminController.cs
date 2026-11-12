using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.RateLimiting;
using TaxVision.Subscription.Application.RateLimiting.Commands.ReviseDefaultRateLimit;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>
/// D12 — perfil por defecto de rate limits: lo que rige para un plan sin fila propia y lo que copia un plan nuevo.
/// Los rate limits son operativos: un cambio aplica a todas las oficinas que dependen del perfil.
/// </summary>
[ApiController]
[Route("admin/subscription/rate-limits/default-profile")]
[Authorize]
[HasPermission(SubscriptionPermissions.CatalogManage)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class DefaultRateLimitProfileAdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<IReadOnlyList<DefaultRateLimitResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<DefaultRateLimitResponse>>>(
            new GetDefaultRateLimitProfileQuery(),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : CatalogErrors.ToResponse(this, result.Error);
    }

    /// <summary>Vista previa de un cambio: actual, propuesto, si es una baja y cuántas oficinas la sentirían.</summary>
    [HttpPost("categories/{category}/preview")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<RateLimitChangePreview>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        string category,
        [FromBody] DefaultRateLimitPreviewRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<RateLimitChangePreview>>(
            new PreviewDefaultRateLimitChangeQuery(category, request.Multiplier),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : CatalogErrors.ToResponse(this, result.Error);
    }

    /// <summary>Subir es directo; bajar exige el token de la vista previa y un motivo. Nunca por debajo de 1.0.</summary>
    [HttpPut("categories/{category}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revise(
        string category,
        [FromBody] ReviseDefaultRateLimitRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ReviseDefaultRateLimitCommand(
                category,
                request.Multiplier,
                userId,
                request.ConfirmationToken,
                request.Reason
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : CatalogErrors.ToResponse(this, result.Error);
    }
}

public sealed record DefaultRateLimitPreviewRequest(decimal Multiplier);

public sealed record ReviseDefaultRateLimitRequest(decimal Multiplier, string? ConfirmationToken, string? Reason);
