using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.PaymentApp;

/// <summary>
/// Implementación de <see cref="IAddOnCheckoutPaymentClient"/> contra <c>POST internal/add-ons/checkout</c> de
/// PaymentApp (M2M ServiceOnly). Molde: <see cref="PaymentAppSeatCheckoutClient"/>.
/// </summary>
internal sealed class PaymentAppAddOnCheckoutClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<PaymentAppAddOnCheckoutClient> logger
) : IAddOnCheckoutPaymentClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<AddOnCheckoutClientResult>> CreateCheckoutAsync(
        AddOnCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<AddOnCheckoutClientResult>(
                new Error("AddOn.Checkout.Unauthorized", "Could not acquire a service token for the payment service.")
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/add-ons/checkout")
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        addOnPurchaseIntentId = request.AddOnPurchaseIntentId,
                        amountCents = request.AmountCents,
                        currency = request.Currency,
                        payerEmail = request.PayerEmail,
                        successUrl = request.SuccessUrl,
                        cancelUrl = request.CancelUrl,
                        idempotencyKey = request.IdempotencyKey,
                        provider = request.Provider,
                        method = request.Method,
                        quantity = request.Quantity,
                        unitAmountCents = request.UnitAmountCents,
                    },
                    options: Json
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("PaymentApp add-on checkout call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<AddOnCheckoutClientResult>(
                    new Error(
                        "AddOn.Checkout.ProviderError",
                        "The payment provider could not create a checkout session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<AddOnCheckoutResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<AddOnCheckoutClientResult>(
                    new Error(
                        "AddOn.Checkout.ProviderError",
                        "The payment provider returned an empty checkout session."
                    )
                );

            return Result.Success(
                new AddOnCheckoutClientResult(
                    payload.PaymentId,
                    payload.CheckoutUrl,
                    payload.ProviderSessionId,
                    payload.ExpiresAtUtc
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentApp add-on checkout call threw for tenant {TenantId}.", request.TenantId);
            return Result.Failure<AddOnCheckoutClientResult>(
                new Error("AddOn.Checkout.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    public async Task<AddOnPaymentStatusResult?> GetPaymentStatusAsync(
        Guid tenantId,
        Guid saaSPaymentId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/add-ons/payments/{saaSPaymentId:D}?tenantId={tenantId:D}"
            );
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "PaymentApp add-on payment status for {PaymentId} returned {Status}.",
                    saaSPaymentId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<AddOnPaymentStatusDto>(Json, ct);
            return dto is null ? null : new AddOnPaymentStatusResult(dto.Status, dto.PaidAtUtc);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp add-on payment status call threw for payment {PaymentId}.",
                saaSPaymentId
            );
            return null;
        }
    }

    private sealed record AddOnCheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );

    private sealed record AddOnPaymentStatusDto(string Status, DateTime? PaidAtUtc);
}
