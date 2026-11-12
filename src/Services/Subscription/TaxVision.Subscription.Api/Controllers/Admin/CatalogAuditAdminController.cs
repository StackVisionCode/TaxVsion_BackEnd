using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Catalog.Audit;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>D13 — auditoría de plataforma del catálogo: quién cambió qué, antes y después, y por qué. Solo lectura.</summary>
[ApiController]
[Route("admin/subscription/catalog/audit")]
[Authorize]
[HasPermission(SubscriptionPermissions.CatalogManage)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class CatalogAuditAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Más nuevas primero; filtra por tipo e id de entidad; pagina con <c>beforeUtc</c> (máximo 200).</summary>
    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<IReadOnlyList<CatalogAuditEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] DateTime? beforeUtc,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<CatalogAuditEntryResponse>>>(
            new GetCatalogAuditQuery(entityType, entityId, beforeUtc, take),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

/// <summary>
/// Errores del catálogo que el mapeo general no cubre: el token viejo es un conflicto (409) y la falta de motivo o de
/// token, un pedido incompleto (400). El resto sigue el mapeo de siempre.
/// </summary>
public static class CatalogErrors
{
    public static IActionResult ToResponse(ControllerBase controller, Error error) =>
        controller.StatusCode(
            error.Code switch
            {
                "Catalog.ConfirmationStale" => StatusCodes.Status409Conflict,
                "Catalog.ReasonRequired"
                or "Catalog.ReasonTooLong"
                or "Catalog.ConfirmationRequired"
                or "RateLimit.MultiplierBelowBase"
                or "RateLimit.MultiplierTooHigh"
                or "RateLimit.InvalidCategory"
                or "RateLimit.CategoryNotScalable" => StatusCodes.Status400BadRequest,
                "Plan.LastOnSale" or "Plan.IsTrialPlan" or "Catalog.PlanHasSubscribers" or "Plan.NotDeprecated" =>
                    StatusCodes.Status409Conflict,
                "Plan.NotFound" => StatusCodes.Status404NotFound,
                _ => error.ToHttpStatusCode(),
            },
            error
        );
}
