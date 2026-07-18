using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Api.Authorization;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Queries;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

[ApiController]
[Route("payments-client/payments")]
[Authorize]
public sealed class TenantPaymentsController(IMessageBus bus) : ControllerBase
{
    public sealed record ChargeTenantPaymentRequest(
        PaymentProviderCode ProviderCode,
        long AmountCents,
        string Currency,
        Guid? TaxpayerId,
        PaymentPurposeKind PurposeKind,
        string? PurposeExternalReferenceId,
        string PaymentMethodReference,
        string? ReceiptEmail,
        string IdempotencyKey,
        long? PlatformFeeAmountCents = null,
        string? PlatformFeeReference = null
    );

    /// <summary>El frontend ya tokenizó la tarjeta con Stripe Elements — este endpoint solo
    /// recibe la referencia opaca resultante, nunca datos crudos de tarjeta.
    /// <see cref="ChargeTenantPaymentRequest.PlatformFeeAmountCents"/> solo aplica si el
    /// config del tenant está en modo Connect.</summary>
    [HttpPost]
    [HasPermission(PaymentClientPermissions.PaymentCharge)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Charge(ChargeTenantPaymentRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new ChargeTenantPaymentCommand(
                tenantId,
                request.ProviderCode,
                request.AmountCents,
                request.Currency,
                request.TaxpayerId,
                request.PurposeKind,
                request.PurposeExternalReferenceId,
                request.PaymentMethodReference,
                request.ReceiptEmail,
                request.IdempotencyKey,
                userId,
                request.PlatformFeeAmountCents,
                request.PlatformFeeReference),
            ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{tenantPaymentId:guid}")]
    [HasPermission(PaymentClientPermissions.PaymentRead)]
    [ProducesResponseType<TenantPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantPaymentId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantPaymentResponse>>(new GetTenantPaymentByIdQuery(tenantId, tenantPaymentId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
