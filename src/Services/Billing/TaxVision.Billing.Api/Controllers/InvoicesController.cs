using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Common;
using TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;
using TaxVision.Billing.Application.Invoices.GetInvoice;
using TaxVision.Billing.Application.Invoices.IssueInvoice;
using TaxVision.Billing.Application.Invoices.ListInvoices;
using TaxVision.Billing.Application.Invoices.RecordManualPayment;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>
/// Facturación tenant→cliente. El tenant y el actor salen siempre del JWT validado, nunca del payload.
/// Fase 1: crear borrador → emitir (genera el PDF vía Documents) → leer.
/// </summary>
[ApiController]
[Route("billing/invoices")]
[Authorize]
public sealed class InvoicesController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateInvoiceDraftRequest(
        InvoiceCustomerInput Customer,
        string Currency,
        IReadOnlyList<InvoiceLineInput> Lines,
        string? Notes,
        InvoiceIssuerInput? Issuer
    );

    [HttpPost]
    [ProducesResponseType<CreateInvoiceDraftResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateDraft(CreateInvoiceDraftRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateInvoiceDraftResult>>(
            new CreateInvoiceDraftCommand(
                tenantId,
                actorId,
                request.Customer,
                request.Currency,
                request.Lines,
                request.Notes,
                request.Issuer
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{invoiceId:guid}/issue")]
    [ProducesResponseType<IssueInvoiceResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Issue(Guid invoiceId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IssueInvoiceResult>>(
            new IssueInvoiceCommand(tenantId, invoiceId, actorId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InvoiceSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<InvoiceSummaryResponse>>>(
            new ListInvoicesQuery(tenantId, take),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RecordManualPaymentRequest(string Method, long? AmountCents, DateTime? PaidAtUtc);

    /// <summary>Registra un pago manual/offline (efectivo, cheque, transferencia…) — marca la factura Paid.</summary>
    [HttpPost("{invoiceId:guid}/record-payment")]
    [ProducesResponseType<RecordManualPaymentResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordManualPayment(
        Guid invoiceId,
        RecordManualPaymentRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<RecordManualPaymentResult>>(
            new RecordManualPaymentCommand(tenantId, invoiceId, request.Method, request.AmountCents, request.PaidAtUtc, actorId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{invoiceId:guid}")]
    [ProducesResponseType<InvoiceSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid invoiceId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InvoiceSummaryResponse>>(
            new GetInvoiceQuery(tenantId, invoiceId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
