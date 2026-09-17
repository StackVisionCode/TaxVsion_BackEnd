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
/// Implementación de <see cref="IRenewalCheckoutPaymentClient"/> contra <c>POST internal/subscription-renewal/checkout</c>
/// de PaymentApp (M2M ServiceOnly). Reusa el <see cref="IGrowthServiceTokenAcquirer"/> (token
/// <c>actor_type=Service</c> vale para la policy ServiceOnly). Molde: <c>PaymentAppSeatCheckoutClient</c>.
/// </summary>
internal sealed class PaymentAppRenewalCheckoutClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<PaymentAppRenewalCheckoutClient> logger
) : IRenewalCheckoutPaymentClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<RenewalCheckoutClientResult>> CreateCheckoutAsync(
        RenewalCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<RenewalCheckoutClientResult>(
                new Error(
                    "Subscription.RenewalCheckout.Unauthorized",
                    "Could not acquire a service token for the payment service."
                )
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/subscription-renewal/checkout")
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        renewalIntentId = request.RenewalIntentId,
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
                logger.LogWarning("PaymentApp renewal checkout call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<RenewalCheckoutClientResult>(
                    new Error(
                        "Subscription.RenewalCheckout.ProviderError",
                        "The payment provider could not create a checkout session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<RenewalCheckoutResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<RenewalCheckoutClientResult>(
                    new Error(
                        "Subscription.RenewalCheckout.ProviderError",
                        "The payment provider returned an empty checkout session."
                    )
                );

            return Result.Success(
                new RenewalCheckoutClientResult(
                    payload.PaymentId,
                    payload.CheckoutUrl,
                    payload.ProviderSessionId,
                    payload.ExpiresAtUtc
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentApp renewal checkout call threw for tenant {TenantId}.", request.TenantId);
            return Result.Failure<RenewalCheckoutClientResult>(
                new Error("Subscription.RenewalCheckout.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    public async Task<RenewalPaymentStatusResult?> GetPaymentStatusAsync(
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
                $"internal/subscription-renewal/payments/{saaSPaymentId:D}?tenantId={tenantId:D}"
            );
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "PaymentApp renewal payment status for {PaymentId} returned {Status}.",
                    saaSPaymentId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<RenewalPaymentStatusDto>(Json, ct);
            return dto is null ? null : new RenewalPaymentStatusResult(dto.Status, dto.PaidAtUtc);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp renewal payment status call threw for payment {PaymentId}.",
                saaSPaymentId
            );
            return null;
        }
    }

    private sealed record RenewalCheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );

    private sealed record RenewalPaymentStatusDto(string Status, DateTime? PaidAtUtc);
}
