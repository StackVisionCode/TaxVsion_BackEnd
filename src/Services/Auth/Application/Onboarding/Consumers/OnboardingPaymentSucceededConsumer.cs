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
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Consumers;

/// <summary>
/// PayFlow (Fase 9) — UoW #3 del plan: cierra el ciclo pago→token. Reacciona al evento que
/// PaymentApp (Fase 8) ya publica. No hay binding nuevo que registrar: la cola
/// <c>auth-tenant-events</c> ya escucha todo el exchange <c>taxvision-events</c> (ver
/// Auth.Api/Program.cs) — Wolverine descubre este handler solo por tipo de mensaje.
/// <para>
/// PayFlow (auditoría F29) — <c>Handle</c> descompuesto en pasos con nombre (cargar+completar el
/// aggregate, emitir el registration token, publicar el evento de registro listo, pedir el
/// recibo) para que cada uno se lea de una sola vez; el flujo secuencial, el orden
/// publish-antes-de-save y los mensajes de log no cambiaron.
/// </para>
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
        IPlanCatalogClient planCatalog,
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
            var onboarding = await LoadAndCompleteOnboardingAsync(evt, onboardings, logger, ct);
            if (onboarding is null)
                return;

            var tokenReference = await IssueRegistrationTokenAsync(onboarding, tokens, tokenReferences, logger, ct);
            if (tokenReference is null)
                return;

            var planName = await planCatalog.GetPlanNameAsync(evt.PlanId, ct);

            await bus.PublishAsync(
                BuildRegistrationReadyEvent(
                    evt,
                    onboarding,
                    tokenReference.Value,
                    planName,
                    onboardingOptions.Value,
                    correlationId
                )
            );

            await unitOfWork.SaveChangesAsync(ct);

            await RequestReceiptGenerationAsync(evt, onboarding, planName, correlationId, receiptDocuments, logger, ct);

            logger.LogInformation(
                "RegistrationToken issued for onboarding {OnboardingId}; TokenReference={TokenReference}.",
                onboarding.Id,
                tokenReference
            );
        }
    }

    private static async Task<TenantOnboarding?> LoadAndCompleteOnboardingAsync(
        OnboardingPaymentSucceededIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        ILogger logger,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(evt.OnboardingId, ct);
        if (onboarding is null)
        {
            logger.LogWarning(
                "OnboardingPaymentSucceeded for unknown onboarding {OnboardingId}; ignoring.",
                evt.OnboardingId
            );
            return null;
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
            return null;
        }

        return onboarding;
    }

    private static async Task<Guid?> IssueRegistrationTokenAsync(
        TenantOnboarding onboarding,
        ISecureTokenService tokens,
        ITokenReferenceStore tokenReferences,
        ILogger logger,
        CancellationToken ct
    )
    {
        var rawToken = tokens.GenerateToken();
        var hashResult = RegistrationTokenHash.Create(tokens.Hash(rawToken));
        if (hashResult.IsFailure)
        {
            logger.LogError(
                "Failed to build RegistrationTokenHash for onboarding {OnboardingId}: {ErrorCode}",
                onboarding.Id,
                hashResult.Error.Code
            );
            return null;
        }

        var setTokenResult = onboarding.SetRegistrationToken(hashResult.Value, DateTime.UtcNow.AddHours(72));
        if (setTokenResult.IsFailure)
        {
            logger.LogWarning(
                "SetRegistrationToken failed for onboarding {OnboardingId}: {ErrorCode}",
                onboarding.Id,
                setTokenResult.Error.Code
            );
            return null;
        }

        return await tokenReferences.StoreAsync(rawToken, ct);
    }

    private static OnboardingRegistrationReadyIntegrationEvent BuildRegistrationReadyEvent(
        OnboardingPaymentSucceededIntegrationEvent evt,
        TenantOnboarding onboarding,
        Guid tokenReference,
        string? planName,
        OnboardingOptions options,
        string correlationId
    ) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = onboarding.Id,
            TokenReference = tokenReference,
            Email = onboarding.Email,
            FirstName = onboarding.FirstName,
            PlanName = planName,
            PriceFormatted = FormatPrice(evt.AmountPaidCents, evt.Currency),
            PaidAtUtc = evt.PaidAtUtc,
            RegistrationUrlBase = options.RegistrationUrlBase,
            CorrelationId = correlationId,
        };

    // PayFlow (Fase 11) — fire-and-forget: la respuesta (ReceiptFileId) llega más tarde vía
    // DocumentGenerationCompletedIntegrationEvent (ver OnboardingReceiptGenerationCompletedConsumer),
    // no bloquea ni revierte el resto del flujo si Documents no responde.
    private static async Task RequestReceiptGenerationAsync(
        OnboardingPaymentSucceededIntegrationEvent evt,
        TenantOnboarding onboarding,
        string? planName,
        string correlationId,
        IReceiptDocumentClient receiptDocuments,
        ILogger logger,
        CancellationToken ct
    )
    {
        var receiptResult = await receiptDocuments.RequestReceiptGenerationAsync(
            new RequestReceiptGenerationRequest(
                OnboardingId: onboarding.Id,
                PayerFirstName: onboarding.FirstName,
                PayerLastName: onboarding.LastName,
                PayerEmail: onboarding.Email,
                PlanName: planName ?? "Selected Plan",
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
    }

    private static string FormatPrice(long amountCents, string currency) =>
        $"{(amountCents / 100m).ToString("F2", CultureInfo.InvariantCulture)} {currency}";

    private static string MaskReference(string reference) => reference.Length <= 4 ? reference : reference[^4..];
}
