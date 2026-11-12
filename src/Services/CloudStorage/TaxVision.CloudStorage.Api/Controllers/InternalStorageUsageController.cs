using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Administration;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Superficie de servicio a servicio: cuánto ocupa la oficina del token. Subscription la consulta antes de mover una
/// oficina a una edición con menos almacenamiento. Solo tokens de servicio; la oficina sale del token, nunca del pedido.
/// </summary>
[ApiController]
[Route("internal/storage/usage")]
[Authorize]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalStorageUsageController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<TenantStorageUsedResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantStorageUsedResponse>>(
            new GetTenantStorageUsedQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
