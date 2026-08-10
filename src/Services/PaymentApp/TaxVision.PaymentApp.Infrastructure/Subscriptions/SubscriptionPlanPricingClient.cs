using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Infrastructure.Subscriptions;

/// <summary>PayFlow (Fase 16) — implementación real de <see cref="ISubscriptionPlanPricingClient"/>.
/// Usa PlatformTenant.Id para el token M2M: el checkout de onboarding ocurre antes de que exista un
/// tenant real (comprador anónimo).</summary>
internal sealed class SubscriptionPlanPricingClient(
    HttpClient httpClient,
    IPaymentAppServiceTokenAcquirer tokenAcquirer,
    ILogger<SubscriptionPlanPricingClient> logger
) : ISubscriptionPlanPricingClient
{
    public async Task<Result<PlanPrice>> GetPriceAsync(
        Guid planId,
        string billingCycle,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<PlanPrice>(
                new Error("PaymentApp.Subscription.Auth", "No Subscription credentials available.")
            );

        // subscription-api sirve rutas LITERALES sin path-base (verificado: /internal/plans/.../pricing →
        // 401=existe; /subscriptions/internal/... → 404). El BaseUrl (SubscriptionClient__BaseUrl) ya es el
        // host. ?cycle= resuelve el precio del ciclo elegido.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"internal/plans/{planId}/pricing?cycle={Uri.EscapeDataString(billingCycle)}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Subscription plan pricing request returned {StatusCode} for plan {PlanId}.",
                    (int)response.StatusCode,
                    planId
                );
                return Result.Failure<PlanPrice>(
                    new Error(
                        "PaymentApp.Subscription.UnexpectedStatus",
                        $"Subscription returned {(int)response.StatusCode}."
                    )
                );
            }

            var payload = await response.Content.ReadFromJsonAsync<PricingDto>(ct);
            return payload is null
                ? Result.Failure<PlanPrice>(
                    new Error("PaymentApp.Subscription.EmptyResponse", "Empty response from Subscription.")
                )
                : Result.Success(new PlanPrice(payload.PriceCents, payload.Currency));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Subscription plan pricing request failed for plan {PlanId}.", planId);
            return Result.Failure<PlanPrice>(
                new Error("PaymentApp.Subscription.RequestFailed", "Could not reach Subscription.")
            );
        }
    }

    private sealed record PricingDto(Guid PlanId, long PriceCents, string Currency);
}
