using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Consumers;

/// <summary>
/// PayFlow (Fase 9) — UoW #3 del plan: cierra el ciclo pago→token. Reacciona al evento que
/// PaymentApp (Fase 8) ya publica. No hay binding nuevo que registrar: la cola
/// <c>auth-tenant-events</c> ya escucha todo el exchange <c>taxvision-events</c> (ver
/// Auth.Api/Program.cs) — Wolverine descubre este handler solo por tipo de mensaje.
/// </summary>
public static class OnboardingPaymentSucceededConsumer
{
    public static async Task Handle(
        OnboardingPaymentSucceededIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        ITokenReferenceStore tokenReferences,
        IOptions<OnboardingOptions> onboardingOptions,
        IReceiptDocumentClient receiptDocuments,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<OnboardingPaymentSucceededIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var onboarding = await onboardings.GetByIdAsync(evt.OnboardingId, ct);
            if (onboarding is null)
            {
                logger.LogWarning(
                    "OnboardingPaymentSucceeded for unknown onboarding {OnboardingId}; ignoring.",
                    evt.OnboardingId
                );
                return;
            }

            var completedResult = onboarding.MarkPaymentCompleted(evt.SaaSPaymentId.ToString("N"), evt.PaidAtUtc);
            if (completedResult.IsFailure)
            {
                // Replay de un evento ya procesado (el onboarding ya avanzó más allá de
                // PaymentCompleted) o inconsistencia real — en ambos casos, no reintentar el
                // resto del flujo (regenerar un token nuevo sería incorrecto en el primer caso).
                logger.LogWarning(
                    "MarkPaymentCompleted failed for onboarding {OnboardingId}: {ErrorCode}",
                    evt.OnboardingId,
                    completedResult.Error.Code
                );
                return;
            }

            var rawToken = tokens.GenerateToken();
            var hashResult = RegistrationTokenHash.Create(tokens.Hash(rawToken));
            if (hashResult.IsFailure)
            {
                logger.LogError(
                    "Failed to build RegistrationTokenHash for onboarding {OnboardingId}: {ErrorCode}",
                    evt.OnboardingId,
                    hashResult.Error.Code
                );
                return;
            }

            var setTokenResult = onboarding.SetRegistrationToken(hashResult.Value, DateTime.UtcNow.AddHours(72));
            if (setTokenResult.IsFailure)
            {
                logger.LogWarning(
                    "SetRegistrationToken failed for onboarding {OnboardingId}: {ErrorCode}",
                    evt.OnboardingId,
                    setTokenResult.Error.Code
                );
                return;
            }

            var tokenReference = await tokenReferences.StoreAsync(rawToken, ct);

            await bus.PublishAsync(
                new OnboardingRegistrationReadyIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = onboarding.Id,
                    TokenReference = tokenReference,
                    Email = onboarding.Email,
                    FirstName = onboarding.FirstName,
                    PlanName = null,
                    PriceFormatted = FormatPrice(evt.AmountPaidCents, evt.Currency),
                    PaidAtUtc = evt.PaidAtUtc,
                    RegistrationUrlBase = onboardingOptions.Value.RegistrationUrlBase,
                    CorrelationId = correlationId,
                }
            );

            await unitOfWork.SaveChangesAsync(ct);

            // PayFlow (Fase 11) — fire-and-forget: la respuesta (ReceiptFileId) llega más tarde vía
            // DocumentGenerationCompletedIntegrationEvent (ver OnboardingReceiptGenerationCompletedConsumer),
            // no bloquea ni revierte el resto del flujo si Documents no responde.
            var receiptResult = await receiptDocuments.RequestReceiptGenerationAsync(
                new RequestReceiptGenerationRequest(
                    OnboardingId: onboarding.Id,
                    PayerFirstName: onboarding.FirstName,
                    PayerLastName: onboarding.LastName,
                    PayerEmail: onboarding.Email,
                    // PlanName real no disponible en Auth hasta Fase 16 (catálogo de planes vive en
                    // Subscription) — mismo gap ya documentado para OnboardingRegistrationReadyIntegrationEvent.PlanName.
                    PlanName: "Selected Plan",
                    PlanCode: evt.PlanId.ToString("N"),
                    PricePaidCents: evt.AmountPaidCents,
                    Currency: evt.Currency,
                    PaidAtUtc: evt.PaidAtUtc,
                    TransactionReferenceMask: MaskReference(evt.ProviderPaymentReference),
                    PaymentMethodMasked: evt.PaymentMethodMasked,
                    IdempotencyKey: $"onboarding-receipt-{onboarding.Id:N}",
                    CorrelationId: correlationId
                ),
                ct
            );
            if (receiptResult.IsFailure)
                logger.LogWarning(
                    "Receipt generation request failed for onboarding {OnboardingId}: {ErrorCode}",
                    onboarding.Id,
                    receiptResult.Error.Code
                );

            logger.LogInformation(
                "RegistrationToken issued for onboarding {OnboardingId}; TokenReference={TokenReference}.",
                onboarding.Id,
                tokenReference
            );
        }
    }

    private static string FormatPrice(long amountCents, string currency) =>
        $"{(amountCents / 100m).ToString("F2", CultureInfo.InvariantCulture)} {currency}";

    private static string MaskReference(string reference) => reference.Length <= 4 ? reference : reference[^4..];
}
