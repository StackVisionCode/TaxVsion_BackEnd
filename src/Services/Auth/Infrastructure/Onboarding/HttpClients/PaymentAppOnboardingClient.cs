using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Security;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 9) — primer HttpClient de Auth que llama a OTRO microservicio de TaxVision (no
/// una API externa). Todo el resto del repo usa un patrón de dos piezas por servicio
/// (<c>{Service}ServiceTokenAcquirer</c> que llama a <c>POST auth/service-token</c> + cachea, y
/// el client HTTP real) — acá se omite deliberadamente: Auth ES el emisor de esos tokens, así
/// que ir por HTTP hasta su propio endpoint de emisión sería un round-trip autoreferencial e
/// inútil. En su lugar, este cliente usa <see cref="OnboardingServiceTokenCache"/> (auditoría
/// F13 — cacheado, no mintado por request), que a su vez mintea con <see cref="IJwtTokenGenerator"/>
/// en el mismo proceso, saltándose además el requisito de <c>TenantId != Guid.Empty</c>
/// que sí aplica a <c>IssueServiceTokenHandler</c> (ese guard vive en el handler que valida
/// credenciales de un caller EXTERNO — no en el generador en sí, y acá no hay ningún caller
/// externo que autenticar: Auth ya sabe que es Auth). El client_id embebido
/// (<c>"auth-onboarding-checkout"</c>) es solo una etiqueta de auditoría en el JWT — PaymentApp
/// no lo valida contra ningún registro, su <c>ServiceOnly</c> policy solo exige
/// <c>actor_type=Service</c>.
/// </summary>
public sealed class PaymentAppOnboardingClient(
    HttpClient httpClient,
    OnboardingServiceTokenCache tokenCache,
    HttpResiliencePipelineRegistry resilience,
    ILogger<PaymentAppOnboardingClient> logger
) : IPaymentAppOnboardingClient
{
    private const string ClientId = "auth-onboarding-checkout";

    // PaymentApp serializa con la política camelCase por defecto de ASP.NET Core; System.Text.Json
    // deserializa case-sensitive por defecto, así que sin esto los campos nunca bindean.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<PaymentAppCheckoutResult>> CreateCheckoutAsync(
        PaymentAppCheckoutRequest request,
        CancellationToken ct = default
    )
    {
        var token = await GetServiceTokenAsync(ct);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/onboarding/checkout")
            {
                Content = JsonContent.Create(
                    new
                    {
                        onboardingId = request.OnboardingId,
                        planId = request.PlanId,
                        payerEmail = request.PayerEmail,
                        successUrl = request.SuccessUrl,
                        cancelUrl = request.CancelUrl,
                        idempotencyKey = request.IdempotencyKey,
                        provider = request.Provider,
                        method = request.Method,
                        billingCycle = request.BillingCycle,
                        netAmountCents = request.NetAmountCents,
                        discountAmountCents = request.DiscountAmountCents,
                        currency = request.Currency,
                        codeReservationId = request.CodeReservationId,
                        promotionSnapshotHash = request.PromotionSnapshotHash,
                    }
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var breaker = resilience.GetOrCreate(nameof(PaymentAppOnboardingClient));
            using var response = await breaker.ExecuteAsync(token => httpClient.SendAsync(httpRequest, token), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp onboarding checkout returned {StatusCode} for onboarding {OnboardingId}.",
                    (int)response.StatusCode,
                    request.OnboardingId
                );
                return Result.Failure<PaymentAppCheckoutResult>(
                    new Error("PaymentAppClient.UnexpectedStatus", $"PaymentApp returned {(int)response.StatusCode}.")
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<PaymentAppCheckoutResponseDto>(ResponseJsonOptions, ct);
            if (dto is null)
                return Result.Failure<PaymentAppCheckoutResult>(
                    new Error("PaymentAppClient.EmptyResponse", "PaymentApp returned an empty checkout response.")
                );

            return Result.Success(
                new PaymentAppCheckoutResult(dto.PaymentId, dto.CheckoutUrl, dto.ProviderSessionId, dto.ExpiresAtUtc)
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp onboarding checkout call failed for onboarding {OnboardingId}.",
                request.OnboardingId
            );
            return Result.Failure<PaymentAppCheckoutResult>(
                new Error("PaymentAppClient.RequestFailed", "Could not reach PaymentApp.")
            );
        }
    }

    public async Task<Result<PaymentAppPaymentOptionsResult>> GetPaymentOptionsAsync(
        PaymentAppPaymentOptionsRequest request,
        CancellationToken ct = default
    )
    {
        var token = await GetServiceTokenAsync(ct);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, BuildPaymentOptionsUri(request));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var breaker = resilience.GetOrCreate(nameof(PaymentAppOnboardingClient));
            using var response = await breaker.ExecuteAsync(token => httpClient.SendAsync(httpRequest, token), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp onboarding payment-options returned {StatusCode} for plan {PlanId}.",
                    (int)response.StatusCode,
                    request.PlanId
                );
                return Result.Failure<PaymentAppPaymentOptionsResult>(
                    new Error("PaymentAppClient.UnexpectedStatus", $"PaymentApp returned {(int)response.StatusCode}.")
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<PaymentAppOptionsResponseDto>(ResponseJsonOptions, ct);
            if (dto is null)
                return Result.Failure<PaymentAppPaymentOptionsResult>(
                    new Error(
                        "PaymentAppClient.EmptyResponse",
                        "PaymentApp returned an empty payment-options response."
                    )
                );

            return Result.Success(
                new PaymentAppPaymentOptionsResult(
                    dto.Options.Select(option => new PaymentAppPaymentOption(
                            option.Provider,
                            option.Method,
                            option.DisplayName,
                            option.Enabled,
                            option.Priority,
                            option.DisabledReason
                        ))
                        .ToArray()
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp onboarding payment-options call failed for plan {PlanId}.",
                request.PlanId
            );
            return Result.Failure<PaymentAppPaymentOptionsResult>(
                new Error("PaymentAppClient.RequestFailed", "Could not reach PaymentApp.")
            );
        }
    }

    public async Task<Result<PaymentAppReconcileResult>> ReconcileCheckoutAsync(
        PaymentAppReconcileRequest request,
        CancellationToken ct = default
    )
    {
        var token = await GetServiceTokenAsync(ct);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "internal/onboarding/reconcile-payment")
            {
                Content = JsonContent.Create(new { paymentId = request.PaymentId }),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var breaker = resilience.GetOrCreate(nameof(PaymentAppOnboardingClient));
            using var response = await breaker.ExecuteAsync(token => httpClient.SendAsync(httpRequest, token), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PaymentApp onboarding reconcile returned {StatusCode} for payment {PaymentId}.",
                    (int)response.StatusCode,
                    request.PaymentId
                );
                return Result.Failure<PaymentAppReconcileResult>(
                    new Error("PaymentAppClient.UnexpectedStatus", $"PaymentApp returned {(int)response.StatusCode}.")
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<PaymentAppReconcileResponseDto>(ResponseJsonOptions, ct);
            if (dto is null)
                return Result.Failure<PaymentAppReconcileResult>(
                    new Error("PaymentAppClient.EmptyResponse", "PaymentApp returned an empty reconcile response.")
                );

            return Result.Success(
                new PaymentAppReconcileResult(
                    dto.PaymentId,
                    dto.Status,
                    dto.AmountPaidCents,
                    dto.Currency,
                    dto.FailureCode,
                    dto.FailureMessage,
                    dto.ProviderPaymentReference,
                    dto.PaidAtUtc
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp onboarding reconcile call failed for payment {PaymentId}.",
                request.PaymentId
            );
            return Result.Failure<PaymentAppReconcileResult>(
                new Error("PaymentAppClient.RequestFailed", "Could not reach PaymentApp.")
            );
        }
    }

    private async Task<AccessToken> GetServiceTokenAsync(CancellationToken ct) =>
        await tokenCache.GetOrCreateAsync(
            PlatformTenant.Id,
            ClientId,
            permissions: [],
            scopes: [],
            audience: "TaxVision.Services",
            lifetimeMinutes: 5,
            ct
        );

    private static string BuildPaymentOptionsUri(PaymentAppPaymentOptionsRequest request)
    {
        var billingCycle = Uri.EscapeDataString(request.BillingCycle);
        var uri = $"internal/onboarding/payment-options?planId={request.PlanId:D}&billingCycle={billingCycle}";
        return string.IsNullOrWhiteSpace(request.Currency)
            ? uri
            : $"{uri}&currency={Uri.EscapeDataString(request.Currency)}";
    }

    private sealed record PaymentAppCheckoutResponseDto(
        Guid PaymentId,
        string CheckoutUrl,
        string ProviderSessionId,
        DateTime ExpiresAtUtc
    );

    private sealed record PaymentAppOptionsResponseDto(IReadOnlyList<PaymentAppOptionDto> Options);

    private sealed record PaymentAppOptionDto(
        string Provider,
        string Method,
        string DisplayName,
        bool Enabled,
        int Priority,
        string? DisabledReason
    );

    private sealed record PaymentAppReconcileResponseDto(
        Guid PaymentId,
        OnboardingPaymentStatus Status,
        long AmountPaidCents,
        string Currency,
        string? FailureCode,
        string? FailureMessage,
        string? ProviderPaymentReference,
        DateTime? PaidAtUtc
    );
}
