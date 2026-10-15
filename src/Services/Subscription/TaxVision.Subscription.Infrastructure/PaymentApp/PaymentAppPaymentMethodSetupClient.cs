using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.PaymentApp;

/// <summary>Implementación de <see cref="IPaymentMethodSetupClient"/> contra
/// <c>POST internal/payment-methods/setup-session</c> de PaymentApp (M2M ServiceOnly). Reusa el
/// <see cref="IGrowthServiceTokenAcquirer"/>. Molde: <c>PaymentAppRenewalCheckoutClient</c>.</summary>
internal sealed class PaymentAppPaymentMethodSetupClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<PaymentAppPaymentMethodSetupClient> logger
) : IPaymentMethodSetupClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<PaymentMethodSetupClientResult>> CreateSetupSessionAsync(
        PaymentMethodSetupClientRequest request,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(request.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<PaymentMethodSetupClientResult>(
                new Error(
                    "Subscription.PaymentMethodSetup.Unauthorized",
                    "Could not acquire a service token for the payment service."
                )
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/payment-methods/setup-session")
            {
                Content = JsonContent.Create(
                    new
                    {
                        tenantId = request.TenantId,
                        payerEmail = request.PayerEmail,
                        successUrl = request.SuccessUrl,
                        cancelUrl = request.CancelUrl,
                        idempotencyKey = request.IdempotencyKey,
                        provider = request.Provider,
                    },
                    options: Json
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp payment-method setup session call failed ({Status}).",
                    (int)response.StatusCode
                );
                return Result.Failure<PaymentMethodSetupClientResult>(
                    new Error(
                        "Subscription.PaymentMethodSetup.ProviderError",
                        "The payment provider could not create a setup session."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<SetupSessionResponseDto>(Json, ct);
            if (payload is null || string.IsNullOrEmpty(payload.CheckoutUrl))
                return Result.Failure<PaymentMethodSetupClientResult>(
                    new Error(
                        "Subscription.PaymentMethodSetup.ProviderError",
                        "The payment provider returned an empty setup session."
                    )
                );

            return Result.Success(new PaymentMethodSetupClientResult(payload.CheckoutUrl, payload.ExpiresAtUtc));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp payment-method setup session call threw for tenant {TenantId}.",
                request.TenantId
            );
            return Result.Failure<PaymentMethodSetupClientResult>(
                new Error("Subscription.PaymentMethodSetup.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    private sealed record SetupSessionResponseDto(string CheckoutUrl, DateTime ExpiresAtUtc);
}
