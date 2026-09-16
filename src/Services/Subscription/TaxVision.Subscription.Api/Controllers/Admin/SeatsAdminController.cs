using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Seats.Commands.SetSeatPrices;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>Autoría del catálogo GLOBAL de precios de asiento — solo PlatformAdmin.</summary>
[ApiController]
[Route("admin/subscription/seats")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class SeatsAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Actualiza el precio mensual/anual de un tipo de asiento (afecta compras nuevas).</summary>
    [HttpPut("{seatType}/prices")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrices(
        string seatType,
        [FromBody] SetSeatPricesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetSeatPricesCommand(seatType, request.MonthlyUsd, request.YearlyUsd, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record SetSeatPricesRequest(decimal MonthlyUsd, decimal YearlyUsd);
