using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentClient.Api.Authorization;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.Recurring.Commands.CancelTenantRecurringPayment;
using TaxVision.PaymentClient.Application.Recurring.Commands.CreateTenantRecurringPayment;
using TaxVision.PaymentClient.Application.Recurring.Commands.PauseTenantRecurringPayment;
using TaxVision.PaymentClient.Application.Recurring.Commands.ResumeTenantRecurringPayment;
using TaxVision.PaymentClient.Application.Recurring.Queries;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

[ApiController]
[Route("payments-client/recurring")]
[Authorize]
public sealed class TenantRecurringPaymentsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateRecurringPaymentRequest(
        Guid TaxpayerId,
        PaymentProviderCode ProviderCode,
        string PaymentMethodReference,
        long AmountCents,
        string Currency,
        PaymentPurposeKind PurposeKind,
        string? PurposeExternalReferenceId,
        BillingCycle BillingCycle,
        int? CustomIntervalDays,
        DateTime StartDate,
        DateTime? EndDate,
        int? MaxExecutions,
        long? PlatformFeeAmountCents,
        string? PlatformFeeReference);

    [HttpPost]
    [HasPermission(PaymentClientPermissions.RecurringManage)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreateRecurringPaymentRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateTenantRecurringPaymentCommand(
                tenantId, request.TaxpayerId, request.ProviderCode, request.PaymentMethodReference,
                request.AmountCents, request.Currency, request.PurposeKind, request.PurposeExternalReferenceId,
                request.BillingCycle, request.CustomIntervalDays, request.StartDate, request.EndDate,
                request.MaxExecutions, request.PlatformFeeAmountCents, request.PlatformFeeReference, userId),
            ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(PaymentClientPermissions.RecurringRead)]
    [ProducesResponseType<IReadOnlyList<TenantRecurringPaymentResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] Guid? taxpayerId, [FromQuery] RecurringStatus? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<TenantRecurringPaymentResponse>>>(
            new SearchTenantRecurringPaymentsQuery(tenantId, taxpayerId, status, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{tenantRecurringPaymentId:guid}")]
    [HasPermission(PaymentClientPermissions.RecurringRead)]
    [ProducesResponseType<TenantRecurringPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantRecurringPaymentId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantRecurringPaymentResponse>>(
            new GetTenantRecurringPaymentByIdQuery(tenantId, tenantRecurringPaymentId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{tenantRecurringPaymentId:guid}/pause")]
    [HasPermission(PaymentClientPermissions.RecurringManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Pause(Guid tenantRecurringPaymentId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new PauseTenantRecurringPaymentCommand(tenantId, tenantRecurringPaymentId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{tenantRecurringPaymentId:guid}/resume")]
    [HasPermission(PaymentClientPermissions.RecurringManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resume(Guid tenantRecurringPaymentId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ResumeTenantRecurringPaymentCommand(tenantId, tenantRecurringPaymentId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelRecurringPaymentRequest(string Reason);

    [HttpPost("{tenantRecurringPaymentId:guid}/cancel")]
    [HasPermission(PaymentClientPermissions.RecurringManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid tenantRecurringPaymentId, CancelRecurringPaymentRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelTenantRecurringPaymentCommand(tenantId, tenantRecurringPaymentId, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
