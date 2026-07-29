using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// PayFlow (Fase 11) — Auth pidiéndole a Documents (Fase 10) que genere el PDF del recibo de
/// onboarding. Mismo patrón deliberado que <see cref="PaymentAppOnboardingClient"/>: Auth es el
/// propio emisor de tokens M2M, así que genera el JWT de servicio en el mismo proceso en vez de
/// hacer un round-trip a su propio endpoint de emisión. El endpoint de Documents
/// (<c>InternalOnboardingReceiptsController</c>) sólo exige <c>actor_type=Service</c> — el
/// <c>client_id</c> embebido es únicamente una etiqueta de auditoría.
///
/// A diferencia de PaymentApp (que espera <c>idempotencyKey</c> como campo del body), este endpoint
/// de Documents la lee de un header <c>Idempotency-Key</c> — mismo contrato que
/// <c>BillingDocumentsClient</c> usa contra el mismo controller family.
/// </summary>
public sealed class ReceiptDocumentClient(
    HttpClient httpClient,
    IJwtTokenGenerator tokens,
    ILogger<ReceiptDocumentClient> logger
) : IReceiptDocumentClient
{
    private const string ClientId = "auth-onboarding-receipt";
    private const string TemplateKey = "onboarding.receipt.v1";
    private const int TemplateVersion = 1;
    private const int DocumentVersion = 1;

    public async Task<Result> RequestReceiptGenerationAsync(
        RequestReceiptGenerationRequest request,
        CancellationToken ct = default
    )
    {
        var token = tokens.GenerateScopedServiceToken(
            PlatformTenant.Id,
            ClientId,
            permissions: [],
            scopes: [],
            audience: "TaxVision.Services",
            lifetimeMinutes: 5
        );

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "internal/document-generations/onboarding-receipts"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        onboardingId = request.OnboardingId,
                        documentVersion = DocumentVersion,
                        templateKey = TemplateKey,
                        templateVersion = TemplateVersion,
                        receipt = new
                        {
                            payerFirstName = request.PayerFirstName,
                            payerLastName = request.PayerLastName,
                            payerEmail = request.PayerEmail,
                            planName = request.PlanName,
                            planCode = request.PlanCode,
                            pricePaidCents = request.PricePaidCents,
                            currency = request.Currency,
                            paidAtUtc = request.PaidAtUtc,
                            transactionReferenceMask = request.TransactionReferenceMask,
                            paymentMethodMasked = request.PaymentMethodMasked,
                        },
                    }
                ),
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
            if (!string.IsNullOrEmpty(request.CorrelationId))
                httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", request.CorrelationId);

            using var response = await httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Documents onboarding-receipt request returned {StatusCode} for onboarding {OnboardingId}.",
                    (int)response.StatusCode,
                    request.OnboardingId
                );
                return Result.Failure(
                    new Error(
                        "ReceiptDocumentClient.UnexpectedStatus",
                        $"Documents returned {(int)response.StatusCode}."
                    )
                );
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Documents onboarding-receipt request failed for onboarding {OnboardingId}.",
                request.OnboardingId
            );
            return Result.Failure(new Error("ReceiptDocumentClient.RequestFailed", "Could not reach Documents."));
        }
    }
}
