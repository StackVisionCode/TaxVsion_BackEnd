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
/// Implementación de <see cref="IAccountPaymentOptionsClient"/> contra
/// <c>GET internal/account/payment-options</c> de PaymentApp (M2M ServiceOnly). Reusa el
/// <see cref="IGrowthServiceTokenAcquirer"/> igual que los demás clientes M2M del Subscription.
/// Molde: <see cref="PaymentAppPaymentMethodSetupClient"/>.
/// </summary>
internal sealed class PaymentAppAccountPaymentOptionsClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<PaymentAppAccountPaymentOptionsClient> logger
) : IAccountPaymentOptionsClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<AccountPaymentOptionsResult>> GetOptionsAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<AccountPaymentOptionsResult>(
                new Error(
                    "Subscription.AccountPaymentOptions.Unauthorized",
                    "Could not acquire a service token for the payment service."
                )
            );

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "internal/account/payment-options");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp account payment-options returned {Status} for tenant {TenantId}.",
                    (int)response.StatusCode,
                    tenantId
                );
                return Result.Failure<AccountPaymentOptionsResult>(
                    new Error(
                        "Subscription.AccountPaymentOptions.ProviderError",
                        "The payment service could not list methods."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<AccountPaymentOptionsResponseDto>(Json, ct);
            if (payload is null)
                return Result.Failure<AccountPaymentOptionsResult>(
                    new Error(
                        "Subscription.AccountPaymentOptions.ProviderError",
                        "The payment service returned an empty response."
                    )
                );

            var options = payload
                .Options.Select(option => new AccountPaymentOption(
                    option.Provider,
                    option.Method,
                    option.DisplayName,
                    option.Enabled,
                    option.Priority,
                    option.DisabledReason
                ))
                .ToArray();
            return Result.Success(new AccountPaymentOptionsResult(options));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PaymentApp account payment-options call threw for tenant {TenantId}.", tenantId);
            return Result.Failure<AccountPaymentOptionsResult>(
                new Error("Subscription.AccountPaymentOptions.Unavailable", "The payment service is unavailable.")
            );
        }
    }

    private sealed record AccountPaymentOptionsResponseDto(IReadOnlyList<AccountPaymentOptionDto> Options);

    private sealed record AccountPaymentOptionDto(
        string Provider,
        string Method,
        string DisplayName,
        bool Enabled,
        int Priority,
        string? DisabledReason
    );
}
