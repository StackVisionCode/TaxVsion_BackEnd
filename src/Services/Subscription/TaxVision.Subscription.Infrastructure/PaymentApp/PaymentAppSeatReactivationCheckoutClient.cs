using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.PurchaseIntentAmount;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.PaymentApp;

/// <summary>Implementación de <see cref="ISeatReactivationCheckoutClient"/> contra
/// <c>POST internal/seats/{seatId}/reactivate-checkout</c> de PaymentApp. Molde idéntico al
/// <see cref="PaymentAppSeatCheckoutClient"/>: token M2M <c>Service</c>, HMAC con
/// <see cref="PurchaseIntentKind.SeatReactivation"/>.</summary>
internal sealed class PaymentAppSeatReactivationCheckoutClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    IPurchaseIntentAmountSigner amountSigner,
    ILogger<PaymentAppSeatReactivationCheckoutClient> logger
) : ISeatReactivationCheckoutClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<SeatCheckoutClientResult>> CreateReactivationCheckoutAsync(
        SeatReactivationCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<SeatCheckoutClientResult>(
                new Error("SeatReactivation.Unauthorized", "Could not acquire a service token for the payment service.")
            );

        var amountSignature = amountSigner.Sign(
            new PurchaseIntentAmountInput(
                request.TenantId,
                PurchaseIntentKind.SeatReactivation,
                request.SeatId,
                request.AmountCents,
                request.Currency
            )
        );

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"internal/seats/{request.SeatId:D}/reactivate-checkout"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        seatId = request.SeatId,
                        amountCents = request.AmountCents,
                        currency = request.Currency,
                        payerEmail = request.PayerEmail,
                        successUrl = request.SuccessUrl,
                        cancelUrl = request.CancelUrl,
                        idempotencyKey = request.IdempotencyKey,
                        provider = request.Provider,
                        method = request.Method,
                        amountSignature,
                    },
                    options: Json
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp seat reactivation checkout failed for seat {SeatId} ({Status}).",
                    request.SeatId,
                    (int)response.StatusCode
                );
                return Result.Failure<SeatCheckoutClientResult>(
                    new Error(
                        "SeatReactivation.ProviderError",
                        "The payment provider could not create a reactivation checkout session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<SeatCheckoutResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<SeatCheckoutClientResult>(
                    new Error(
                        "SeatReactivation.ProviderError",
                        "The payment provider returned an empty reactivation checkout session."
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
            logger.LogWarning(
                ex,
                "PaymentApp seat reactivation checkout call threw for seat {SeatId}.",
                request.SeatId
            );
            return Result.Failure<SeatCheckoutClientResult>(
                new Error("SeatReactivation.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    private sealed record SeatCheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );
}
