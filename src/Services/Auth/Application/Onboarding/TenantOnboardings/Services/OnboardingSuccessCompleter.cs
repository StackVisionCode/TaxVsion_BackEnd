using System.Globalization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>
/// Camino de éxito COMPARTIDO del onboarding (una vez que la operación quedó completada: pago exitoso o
/// cobertura 100% por código). Emite el RegistrationToken, publica <c>OnboardingRegistrationReady</c>
/// (para el email con el link /register), encola el <c>OnboardingFinalizeCommand</c> (commit de códigos
/// + qualify + factura en Billing) y el <c>RequestOnboardingReceiptCommand</c> (recibo de pago al
/// comprador, cortesía distinta de la factura). NO hace SaveChanges — eso lo hace el caller
/// (publish-before-save, outbox de Wolverine). Reusado por el consumer de pago (net &gt; 0) y por el
/// carril $0 en el checkout.
/// </summary>
public sealed class OnboardingSuccessCompleter(
    ISecureTokenService tokens,
    ITokenReferenceStore tokenReferences,
    IOptions<OnboardingOptions> onboardingOptions,
    IMessageBus bus,
    ILogger<OnboardingSuccessCompleter> logger
)
{
    public async Task<Result> CompleteAsync(
        TenantOnboarding onboarding,
        long amountPaidCents,
        string currency,
        Guid? paymentId,
        string? planName,
        DateTime paidAtUtc,
        string? providerPaymentReference,
        string? paymentMethodMasked,
        string correlationId,
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
            return Result.Failure(hashResult.Error);
        }

        var setToken = onboarding.SetRegistrationToken(hashResult.Value, DateTime.UtcNow.AddHours(72));
        if (setToken.IsFailure)
        {
            logger.LogWarning(
                "SetRegistrationToken failed for onboarding {OnboardingId}: {ErrorCode}",
                onboarding.Id,
                setToken.Error.Code
            );
            return Result.Failure(setToken.Error);
        }

        var tokenReference = await tokenReferences.StoreAsync(rawToken, ct);

        bus.TenantId = PlatformTenant.Id.ToString();
        await bus.PublishAsync(
            new OnboardingRegistrationReadyIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = onboarding.Id,
                TokenReference = tokenReference,
                Email = onboarding.Email,
                FirstName = onboarding.FirstName,
                PlanName = planName,
                PriceFormatted = FormatPrice(amountPaidCents, currency),
                PaidAtUtc = paidAtUtc,
                RegistrationUrlBase = onboardingOptions.Value.RegistrationUrlBase,
                CorrelationId = correlationId,
            }
        );

        // FINALIZE (Billing invoice + Growth commit/qualify) como comando local AUTO-CONTENIDO: lleva el
        // desglose y las reservas tomados del onboarding EN MEMORIA (fresco), para no depender de recargar
        // el aggregate (que podría leer datos previos al commit). Sin códigos: bruto = neto = lo cobrado.
        var gross = onboarding.GrossAmountCents ?? amountPaidCents;
        var discount = onboarding.TotalDiscountCents ?? 0;
        var net = onboarding.NetAmountCents ?? amountPaidCents;
        var settlement =
            net == 0 ? "FullyCoveredByCode"
            : discount > 0 ? "Mixed"
            : "Paid";
        var reservations = onboarding
            .CodeReservations.OrderBy(r => r.Order)
            .Select(r => new FinalizeReservationDto(
                r.CodeReservationId,
                r.BenefitType.ToString(),
                r.Code,
                r.DiscountCents,
                r.SnapshotHash,
                r.Order
            ))
            .ToList();

        var planLabel = string.IsNullOrWhiteSpace(planName) ? "Suscripción TaxProffice" : planName!;
        var receiptCurrency = onboarding.Currency ?? currency;

        await bus.PublishAsync(
            new OnboardingFinalizeCommand(
                onboarding.Id,
                onboarding.PlanId,
                planLabel,
                $"{onboarding.FirstName} {onboarding.LastName}".Trim(),
                onboarding.Email,
                paymentId,
                gross,
                discount,
                net,
                receiptCurrency,
                settlement,
                onboarding.ReferralAttributionId,
                reservations
            )
        );

        // Recibo de pago para el comprador (cortesía, distinto de la factura de Billing). Comando local
        // reintentable e idempotente en Documents; el monto es lo efectivamente cobrado (0 en el carril $0).
        await bus.PublishAsync(
            new RequestOnboardingReceiptCommand(
                onboarding.Id,
                onboarding.FirstName,
                onboarding.LastName,
                onboarding.Email,
                planLabel,
                amountPaidCents,
                receiptCurrency,
                paidAtUtc,
                providerPaymentReference,
                paymentMethodMasked,
                correlationId
            )
        );

        logger.LogInformation(
            "Onboarding {OnboardingId} success path completed (paymentId={PaymentId}); TokenReference={TokenReference}.",
            onboarding.Id,
            paymentId,
            tokenReference
        );
        return Result.Success();
    }

    private static string FormatPrice(long amountCents, string currency) =>
        $"{(amountCents / 100m).ToString("F2", CultureInfo.InvariantCulture)} {currency}";
}
