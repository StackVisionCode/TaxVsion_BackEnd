using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.AddOns.Commands.CreateModuleAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>Autoría del catálogo de add-ons — solo PlatformAdmin.</summary>
[ApiController]
[Route("admin/subscription/addons")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class AddOnsAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Crea y publica un add-on de módulo con su precio mensual/anual. Un add-on de un módulo
    /// NUEVO (prefijo aún no mapeado en PermissionModuleMap) exige agregar antes ese prefijo en código
    /// + deploy; los módulos ya existentes se venden sin tocar código.</summary>
    [HttpPost]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateModuleAddOnRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateModuleAddOnCommand(
                request.Code,
                request.Name,
                request.Module,
                request.MonthlyUsd,
                request.YearlyUsd,
                userId
            ),
            ct
        );
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Actualiza el precio de un add-on (afecta compras nuevas).</summary>
    [HttpPut("{addOnId:guid}/prices")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrices(
        Guid addOnId,
        [FromBody] SetAddOnPricesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetAddOnPricesCommand(addOnId, request.MonthlyUsd, request.YearlyUsd, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record CreateModuleAddOnRequest(
    string Code,
    string Name,
    string Module,
    decimal MonthlyUsd,
    decimal YearlyUsd
);

public sealed record SetAddOnPricesRequest(decimal MonthlyUsd, decimal YearlyUsd);
