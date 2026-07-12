using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.AddOns.Commands.CancelAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Application.AddOns.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("addons")]
[Authorize]
public sealed class AddOnsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Catálogo público de add-ons.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    [ProducesResponseType<IReadOnlyList<AddOnDefinitionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<AddOnDefinitionResponse>>>(new GetAddOnCatalogQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Add-ons vigentes del tenant autenticado.</summary>
    [HttpGet("tenant")]
    [ProducesResponseType<IReadOnlyList<AddOnResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantAddOns(CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AddOnResponse>>>(new GetTenantAddOnsQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record PurchaseAddOnRequest(string AddOnCode, int Quantity, bool AutoRenew);

    [HttpPost]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Purchase(PurchaseAddOnRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new PurchaseAddOnCommand(tenantId, request.AddOnCode, request.Quantity, request.AutoRenew, userId), ct);

        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelAddOnRequest(string Reason);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancelAddOnRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelAddOnCommand(tenantId, id, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool TryGetTenantAndUser(out Guid tenantId, out Guid userId)
    {
        userId = Guid.Empty;
        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId))
            return false;

        var raw =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }
}
