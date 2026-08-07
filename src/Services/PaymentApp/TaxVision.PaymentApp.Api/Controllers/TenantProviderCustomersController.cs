using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.AttachPaymentMethod;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.CreateSetupIntent;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.DetachPaymentMethod;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.SetDefaultPaymentMethod;
using TaxVision.PaymentApp.Application.ProviderCustomers.Queries;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

[ApiController]
[Route("payments-app/provider-customers")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TenantProviderCustomersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{provider}")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerRead)]
    [RateLimit("payment_app.f.provider_customer_read")]
    [ProducesResponseType<TenantProviderCustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(PaymentProviderCode provider, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantProviderCustomerResponse>>(
            new GetTenantProviderCustomerQuery(tenantId, provider),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Crea un SetupIntent para que el frontend recolecte la tarjeta con Stripe Payment
    /// Element (el PAN va directo a Stripe, nunca al backend). Devuelve el client_secret a confirmar;
    /// el pm resultante se envía luego a POST {provider}/methods.</summary>
    [HttpPost("{provider}/setup-intent")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [RateLimit("payment_app.l.provider_customer_write")]
    [ProducesResponseType<SetupIntentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSetupIntent(PaymentProviderCode provider, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SetupIntentResponse>>(
            new CreateSetupIntentCommand(tenantId, provider),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record AttachPaymentMethodRequest(string PaymentMethodReference, bool SetAsDefault);

    /// <summary>El frontend ya tokenizó la tarjeta con Stripe Elements / SetupIntent — este
    /// endpoint solo recibe la referencia opaca resultante, nunca datos crudos de tarjeta.</summary>
    [HttpPost("{provider}/methods")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [RateLimit("payment_app.l.provider_customer_write")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachMethod(
        PaymentProviderCode provider,
        AttachPaymentMethodRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new AttachPaymentMethodCommand(
                tenantId,
                provider,
                request.PaymentMethodReference,
                request.SetAsDefault,
                userId
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{tenantProviderCustomerId:guid}/methods/{methodId:guid}")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [RateLimit("payment_app.l.provider_customer_write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DetachMethod(Guid tenantProviderCustomerId, Guid methodId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DetachPaymentMethodCommand(tenantId, tenantProviderCustomerId, methodId, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{tenantProviderCustomerId:guid}/methods/{methodId:guid}/default")]
    [HasPermission(PaymentAppPermissions.ProviderCustomerManage)]
    [RateLimit("payment_app.g.provider_customer_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefaultMethod(
        Guid tenantProviderCustomerId,
        Guid methodId,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetDefaultPaymentMethodCommand(tenantId, tenantProviderCustomerId, methodId, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
