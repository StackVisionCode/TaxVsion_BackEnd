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
/// Implementación de <see cref="ISeatCheckoutPaymentClient"/> contra <c>POST internal/seats/checkout</c> de
/// PaymentApp (M2M ServiceOnly). Reusa el <see cref="IGrowthServiceTokenAcquirer"/> (acquirer genérico de
/// tokens de servicio de Auth — el token <c>actor_type=Service</c> vale para la policy ServiceOnly de
/// PaymentApp). Molde: <c>PaymentAppOnboardingClient</c> de Auth.
/// </summary>
internal sealed class PaymentAppSeatCheckoutClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<PaymentAppSeatCheckoutClient> logger
) : ISeatCheckoutPaymentClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<SeatCheckoutClientResult>> CreateCheckoutAsync(
        SeatCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<SeatCheckoutClientResult>(
                new Error("Seats.Checkout.Unauthorized", "Could not acquire a service token for the payment service.")
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/seats/checkout")
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        seatPurchaseIntentId = request.SeatPurchaseIntentId,
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
                logger.LogWarning("PaymentApp seat checkout call failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<SeatCheckoutClientResult>(
                    new Error(
                        "Seats.Checkout.ProviderError",
                        "The payment provider could not create a checkout session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<SeatCheckoutResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<SeatCheckoutClientResult>(
                    new Error(
                        "Seats.Checkout.ProviderError",
                        "The payment provider returned an empty checkout session."
                    )
                );

            return Result.Success(
                new SeatCheckoutClientResult(
                    payload.PaymentId,
                    payload.CheckoutUrl,
                    payload.ProviderSessionId,
                    payload.ExpiresAtUtc
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentApp seat checkout call threw for tenant {TenantId}.", request.TenantId);
            return Result.Failure<SeatCheckoutClientResult>(
                new Error("Seats.Checkout.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    public async Task<SeatPaymentStatusResult?> GetPaymentStatusAsync(
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
                $"internal/seats/payments/{saaSPaymentId:D}?tenantId={tenantId:D}"
            );
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "PaymentApp seat payment status for {PaymentId} returned {Status}.",
                    saaSPaymentId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<SeatPaymentStatusDto>(Json, ct);
            return dto is null ? null : new SeatPaymentStatusResult(dto.Status, dto.PaidAtUtc);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentApp seat payment status call threw for payment {PaymentId}.", saaSPaymentId);
            return null;
        }
    }

    private sealed record SeatCheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );

    private sealed record SeatPaymentStatusDto(string Status, DateTime? PaidAtUtc);
}
