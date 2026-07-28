using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Infrastructure.ServiceAuth;

namespace TaxVision.Billing.Infrastructure.Payments;

/// <summary>
/// Cliente M2M hacia PaymentClient (POST internal/payables/invoices, semántica ensure/idempotente).
/// Adjunta el token de servicio (audience taxvision-payments, scope payments.links.create). PaymentClient
/// es dueño de la URL: devuelve la <c>checkoutUrl</c> absoluta y estable ya compuesta; Billing solo la
/// guarda. El link con token se acuña perezosamente cuando el taxpayer abre esa URL.
/// </summary>
public sealed class BillingPaymentClient(
    HttpClient http,
    IServiceTokenProvider tokenProvider,
    ILogger<BillingPaymentClient> logger
) : IInvoicePaymentLinkClient
{
    public async Task<Result<InvoicePayableResult>> EnsurePayableAsync(
        long amountCents,
        string currency,
        Guid invoiceId,
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var token = await tokenProvider.GetTokenAsync("payments", tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<InvoicePayableResult>(
                new Error("Billing.PaymentClient.TokenFailed", "Could not acquire a service token for PaymentClient.")
            );

        var body = new { amountCents, currency, invoiceId = invoiceId.ToString() };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/payables/invoices")
        {
            Content = JsonContent.Create(body),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning(
                    "PaymentClient ensure-payable for invoice {InvoiceId} failed: {Status} {Body}",
                    invoiceId,
                    (int)response.StatusCode,
                    errorBody
                );
                return Result.Failure<InvoicePayableResult>(
                    new Error("Billing.PaymentClient.EnsureFailed", $"PaymentClient returned {(int)response.StatusCode}.")
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<EnsurePayableDto>(ct);
            if (dto is null || dto.PayableId == Guid.Empty || string.IsNullOrEmpty(dto.CheckoutUrl))
                return Result.Failure<InvoicePayableResult>(
                    new Error("Billing.PaymentClient.BadResponse", "PaymentClient returned an empty payable.")
                );

            return Result.Success(new InvoicePayableResult(dto.PayableId, dto.CheckoutUrl));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentClient ensure-payable for invoice {InvoiceId} threw.", invoiceId);
            return Result.Failure<InvoicePayableResult>(
                new Error("Billing.PaymentClient.Unreachable", "PaymentClient service is unreachable.")
            );
        }
    }

    private sealed record EnsurePayableDto(Guid PayableId, string Reference, string CheckoutUrl);
}
