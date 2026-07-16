using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Authorization;
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Queries;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

[ApiController]
[Route("payments-app/saas-payments")]
[Authorize]
public sealed class SaaSPaymentsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [HasPermission(PaymentAppPermissions.SaaSPaymentRead)]
    [ProducesResponseType<SaaSPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SaaSPaymentResponse>>(new GetSaaSPaymentByIdQuery(tenantId, id), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RefundRequest(long RefundAmountCents, string Reason);

    /// <summary>Reembolso administrativo, solo plataforma — un tenant nunca se reembolsa a
    /// sí mismo.</summary>
    [HttpPost("{id:guid}/refund")]
    [HasPermission(PaymentAppPermissions.SaaSPaymentRefund)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Refund(Guid id, RefundRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RefundSaaSPaymentCommand(tenantId, id, request.RefundAmountCents, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
