using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Wallet.Abstractions;

namespace TaxVision.Wallet.Infrastructure.PaymentApp;

/// <summary>
/// Implementación de <see cref="IWalletTopUpCheckoutClient"/> contra <c>POST internal/wallet/top-ups/checkout</c>
/// de PaymentApp (M2M ServiceOnly). Reusa el <see cref="IServiceTokenAcquirer"/> ya registrado en el Wallet
/// (token <c>actor_type=Service</c>, válido para la policy ServiceOnly de PaymentApp). Molde:
/// <c>PaymentAppSeatCheckoutClient</c> de Subscription.
/// </summary>
internal sealed class WalletTopUpCheckoutClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<WalletTopUpCheckoutClient> logger
) : IWalletTopUpCheckoutClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<WalletTopUpCheckoutClientResult>> CreateCheckoutAsync(
        WalletTopUpCheckoutClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<WalletTopUpCheckoutClientResult>(
                new Error(
                    "WalletTopUp.Checkout.Unauthorized",
                    "Could not acquire a service token for the payment service."
                )
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/wallet/top-ups/checkout")
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        topUpId = request.TopUpId,
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
                logger.LogWarning(
                    "PaymentApp wallet top-up checkout call failed ({Status}).",
                    (int)response.StatusCode
                );
                return Result.Failure<WalletTopUpCheckoutClientResult>(
                    new Error(
                        "WalletTopUp.Checkout.ProviderError",
                        "The payment provider could not create a checkout session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<CheckoutResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<WalletTopUpCheckoutClientResult>(
                    new Error(
                        "WalletTopUp.Checkout.ProviderError",
                        "The payment provider returned an empty checkout session."
                    )
                );

            return Result.Success(
                new WalletTopUpCheckoutClientResult(
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
                "PaymentApp wallet top-up checkout call threw for tenant {TenantId}.",
                request.TenantId
            );
            return Result.Failure<WalletTopUpCheckoutClientResult>(
                new Error("WalletTopUp.Checkout.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    private sealed record CheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );
}
