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
/// Implementación de <see cref="IPlanChangeCheckoutPaymentClient"/> contra
/// <c>POST internal/plan-change/checkout</c> de PaymentApp (M2M ServiceOnly). Molde:
/// <see cref="PaymentAppSeatCheckoutClient"/>.
/// </summary>
internal sealed class PaymentAppPlanChangeCheckoutClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<PaymentAppPlanChangeCheckoutClient> logger
) : IPlanChangeCheckoutPaymentClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<PlanChangeCheckoutClientResult>> CreateCheckoutAsync(
        PlanChangeCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<PlanChangeCheckoutClientResult>(
                new Error(
                    "PlanChange.Checkout.Unauthorized",
                    "Could not acquire a service token for the payment service."
                )
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/plan-change/checkout")
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        planChangeRequestId = request.PlanChangeRequestId,
                        amountCents = request.AmountCents,
                        currency = request.Currency,
                        payerEmail = request.PayerEmail,
                        successUrl = request.SuccessUrl,
                        cancelUrl = request.CancelUrl,
                        idempotencyKey = request.IdempotencyKey,
                        provider = request.Provider,
                        method = request.Method,
                    },
                    options: Json
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("PaymentApp plan change checkout call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<PlanChangeCheckoutClientResult>(
                    new Error(
                        "PlanChange.Checkout.ProviderError",
                        "The payment provider could not create a checkout session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<PlanChangeCheckoutResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<PlanChangeCheckoutClientResult>(
                    new Error(
                        "PlanChange.Checkout.ProviderError",
                        "The payment provider returned an empty checkout session."
                    )
                );

            return Result.Success(
                new PlanChangeCheckoutClientResult(
                    payload.PaymentId,
                    payload.CheckoutUrl,
                    payload.ProviderSessionId,
                    payload.ExpiresAtUtc
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentApp plan change checkout threw for tenant {TenantId}.", request.TenantId);
            return Result.Failure<PlanChangeCheckoutClientResult>(
                new Error("PlanChange.Checkout.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    private sealed record PlanChangeCheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );
}
