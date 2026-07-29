using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Infrastructure.ServiceAuth;

namespace TaxVision.Billing.Infrastructure.Documents;

/// <summary>
/// Cliente M2M hacia Documents. Mapea el request de Billing al contrato exacto de Documents
/// (POST internal/document-generations/invoices) — TemplateKey "billing.invoice.v1" v1, montos en
/// decimales — y adjunta el token de servicio + Idempotency-Key. Documents responde 202; el FileId
/// llega luego por documents.generation.completed.
/// </summary>
public sealed class BillingDocumentsClient(
    HttpClient http,
    IServiceTokenProvider tokenProvider,
    ILogger<BillingDocumentsClient> logger
) : IInvoiceDocumentClient
{
    public async Task<Result> GenerateAsync(
        InvoiceDocumentRequest request,
        Guid tenantId,
        string idempotencyKey,
        string? correlationId,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetTokenAsync("documents", tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure(
                new Error("Billing.Documents.TokenFailed", "Could not acquire a service token for Documents.")
            );

        var body = new
        {
            invoiceId = request.InvoiceId,
            invoiceNumber = request.InvoiceNumber,
            documentVersion = 1,
            templateKey = "billing.invoice.v1",
            templateVersion = 1,
            taxYear = request.TaxYear,
            invoice = new
            {
                currency = request.Currency,
                issueDate = request.IssueDate,
                dueDate = request.DueDate,
                issuer = new
                {
                    name = request.Issuer.Name,
                    taxId = request.Issuer.TaxId,
                    address = request.Issuer.Address,
                },
                customer = new
                {
                    name = request.Customer.Name,
                    taxId = request.Customer.TaxId,
                    address = request.Customer.Address,
                },
                lines = request.Lines.Select(l => new
                {
                    description = l.Description,
                    quantity = l.Quantity,
                    unitPrice = l.UnitPrice,
                    amount = l.Amount,
                }),
                subtotal = request.Subtotal,
                taxAmount = request.TaxAmount,
                total = request.Total,
                notes = request.Notes,
                status = request.Status,
                paymentUrl = request.PaymentUrl,
                paidDate = request.PaidDate,
                receiptNumber = request.ReceiptNumber,
                receiptHash = request.ReceiptHash,
            },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/document-generations/invoices")
        {
            Content = JsonContent.Create(body),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (!string.IsNullOrEmpty(correlationId))
            httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        try
        {
            using var response = await http.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning(
                    "Documents generate for invoice {InvoiceId} failed: {Status} {Body}",
                    request.InvoiceId,
                    (int)response.StatusCode,
                    errorBody
                );
                return Result.Failure(
                    new Error("Billing.Documents.GenerateFailed", $"Documents returned {(int)response.StatusCode}.")
                );
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Documents generate for invoice {InvoiceId} threw.", request.InvoiceId);
            return Result.Failure(new Error("Billing.Documents.Unreachable", "Documents service is unreachable."));
        }
    }
}
