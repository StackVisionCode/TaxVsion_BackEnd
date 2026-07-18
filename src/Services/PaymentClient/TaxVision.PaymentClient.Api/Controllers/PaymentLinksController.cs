using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Api.Authorization;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.CreatePaymentLink;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RevokePaymentLink;
using TaxVision.PaymentClient.Application.PaymentLinks.Queries;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

[ApiController]
[Route("payments-client/payment-links")]
[Authorize]
public sealed class PaymentLinksController(IMessageBus bus) : ControllerBase
{
    public sealed record CreatePaymentLinkRequest(
        Guid? TaxpayerId,
        long AmountCents,
        string Currency,
        PaymentPurposeKind PurposeKind,
        string? PurposeExternalReferenceId,
        TimeSpan Expiration);

    [HttpPost]
    [HasPermission(PaymentClientPermissions.PaymentLinkManage)]
    [ProducesResponseType<CreatePaymentLinkResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreatePaymentLinkRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreatePaymentLinkResponse>>(
            new CreatePaymentLinkCommand(
                tenantId, request.TaxpayerId, request.AmountCents, request.Currency,
                request.PurposeKind, request.PurposeExternalReferenceId, request.Expiration, userId),
            ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(PaymentClientPermissions.PaymentLinkRead)]
    [ProducesResponseType<IReadOnlyList<PaymentLinkResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] PaymentLinkStatus? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<PaymentLinkResponse>>>(
            new SearchPaymentLinksQuery(tenantId, status, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RevokePaymentLinkRequest(string Reason);

    [HttpPost("{paymentLinkId:guid}/revoke")]
    [HasPermission(PaymentClientPermissions.PaymentLinkManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid paymentLinkId, RevokePaymentLinkRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RevokePaymentLinkCommand(tenantId, paymentLinkId, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
