using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Api.Authorization;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.Payouts.Commands.UpsertPayoutSchedule;
using TaxVision.PaymentClient.Application.Payouts.Queries;
using TaxVision.PaymentClient.Domain.Payouts;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

/// <summary>Stripe ejecuta el payout en sí (§19.1 del diseño) — este controller solo expone
/// la preferencia de calendario y el ledger de payouts ya ejecutados.</summary>
[ApiController]
[Route("payments-client/payouts")]
[Authorize]
public sealed class PayoutsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(PaymentClientPermissions.PayoutRead)]
    [ProducesResponseType<PayoutScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PayoutScheduleResponse>>(new GetPayoutScheduleQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpsertScheduleRequest(PayoutFrequency Frequency, int? Anchor, string Currency);

    [HttpPut]
    [HasPermission(PaymentClientPermissions.PayoutManage)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert(UpsertScheduleRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new UpsertPayoutScheduleCommand(tenantId, request.Frequency, request.Anchor, request.Currency, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
