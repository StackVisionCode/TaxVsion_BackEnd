using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Queries;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

[ApiController]
[Route("payments-app/saas-payments")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SaaSPaymentsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [HasPermission(PaymentAppPermissions.SaaSPaymentRead)]
    [RateLimit("payment_app.f.saas_payment_read")]
    [ProducesResponseType<SaaSPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SaaSPaymentResponse>>(new GetSaaSPaymentByIdQuery(tenantId, id), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
