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
    /// <summary>Historial de pagos del tenant, el más reciente primero. Lo lee el Account; la lectura no
    /// necesita permiso de admin, solo pertenecer al tenant y poder ver su facturación.</summary>
    [HttpGet]
    [HasPermission(PaymentAppPermissions.SaaSPaymentRead)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("payment_app.f.saas_payment_read")]
    [ProducesResponseType<MySaaSPaymentsPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchMine([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MySaaSPaymentsPage>>(
            new SearchMySaaSPaymentsQuery(tenantId, page <= 0 ? 1 : page, pageSize is <= 0 or > 100 ? 25 : pageSize),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>URL firmada del recibo. El archivo no se sirve desde acá: se valida que el pago es de este
    /// tenant y CloudStorage firma una URL de vida corta.</summary>
    [HttpGet("{id:guid}/receipt")]
    [HasPermission(PaymentAppPermissions.SaaSPaymentRead)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("payment_app.f.saas_payment_read")]
    [ProducesResponseType<ReceiptDownloadUrlResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReceipt(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReceiptDownloadUrlResponse>>(
            new GetReceiptDownloadUrlQuery(tenantId, id),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

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
