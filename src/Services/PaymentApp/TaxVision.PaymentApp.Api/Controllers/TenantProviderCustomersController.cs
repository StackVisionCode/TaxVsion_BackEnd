using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Authorization;
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.AttachPaymentMethod;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.DetachPaymentMethod;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.SetDefaultPaymentMethod;
using TaxVision.PaymentApp.Application.ProviderCustomers.Queries;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

[ApiController]
[Route("payments-app/provider-customers")]
[Authorize]
public sealed class TenantProviderCustomersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{provider}")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerRead)]
    [ProducesResponseType<TenantProviderCustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(PaymentProviderCode provider, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantProviderCustomerResponse>>(new GetTenantProviderCustomerQuery(tenantId, provider), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record AttachPaymentMethodRequest(string PaymentMethodReference, bool SetAsDefault);

    /// <summary>El frontend ya tokenizó la tarjeta con Stripe Elements / SetupIntent — este
    /// endpoint solo recibe la referencia opaca resultante, nunca datos crudos de tarjeta.</summary>
    [HttpPost("{provider}/methods")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachMethod(PaymentProviderCode provider, AttachPaymentMethodRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new AttachPaymentMethodCommand(tenantId, provider, request.PaymentMethodReference, request.SetAsDefault, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{tenantProviderCustomerId:guid}/methods/{methodId:guid}")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DetachMethod(Guid tenantProviderCustomerId, Guid methodId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DetachPaymentMethodCommand(tenantId, tenantProviderCustomerId, methodId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{tenantProviderCustomerId:guid}/methods/{methodId:guid}/default")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefaultMethod(Guid tenantProviderCustomerId, Guid methodId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetDefaultPaymentMethodCommand(tenantId, tenantProviderCustomerId, methodId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
