using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>
/// FINALIZE de la operación comercial del onboarding — se invoca vía <see cref="OnboardingFinalizeCommand"/>
/// (auto-contenido, sin recargar el aggregate). Responsabilidad única de Auth como orquestador: (1) commit
/// de TODAS las reservas de código en Growth (idempotente), (2) qualify del referido SOLO si net &gt; 0 y hay
/// atribución, (3) pide a Billing (fuente de verdad financiera) asentar la factura vía
/// <see cref="OnboardingInvoiceRequestedIntegrationEvent"/>. Billing crea la Invoice en TODOS los casos
/// (incl. total $0 sin PaymentId) y Documents la renderiza. Auth NO genera documentos.
/// </summary>
public sealed class OnboardingFinalizer(
    IGrowthOnboardingClient growth,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<OnboardingFinalizer> logger
)
{
    public async Task<Result> FinalizeAsync(OnboardingFinalizeCommand cmd, CancellationToken ct)
    {
        // El OnboardingId es un source-event estable para el commit/qualify (idempotencia + fingerprint).
        var sourceEventId = cmd.OnboardingId;

        // 1) Commit de todas las reservas (idempotente por reserva). Un fallo se propaga → Wolverine reintenta.
        foreach (var reservation in cmd.Reservations)
        {
            var commit = await growth.CommitAsync(
                reservation.CodeReservationId,
                // Misma referencia de pago única que usó ReserveAsync para esta posición (stacking).
                OnboardingPaymentReference.For(cmd.OnboardingId, reservation.Order),
                reservation.SnapshotHash,
                sourceEventId,
                idempotencyKey: $"onb-commit:{cmd.OnboardingId:N}:{reservation.CodeReservationId:N}",
                ct
            );
            if (commit.IsFailure)
                return commit;
        }

        // 2) Qualify del referido: solo con ingreso real (net > 0) y atribución presente. No bloquea la factura.
        if (cmd.NetAmountCents > 0 && cmd.PaymentId is { } pid && cmd.ReferralAttributionId is { } attributionId)
        {
            var qualify = await growth.QualifyReferralAsync(
                new GrowthQualifyRequest(
                    attributionId,
                    sourceEventId,
                    pid,
                    cmd.NetAmountCents,
                    cmd.Currency,
                    IsFirstSuccessfulPayment: true,
                    DateTime.UtcNow
                ),
                ct
            );
            if (qualify.IsFailure)
                logger.LogWarning(
                    "Referral qualify failed for onboarding {OnboardingId}: {Code} (invoice not blocked).",
                    cmd.OnboardingId,
                    qualify.Error.Code
                );
        }

        // 3) Billing asienta la factura (fuente de verdad financiera). Documents la renderiza.
        var adjustments = cmd
            .Reservations.Select(r => new OnboardingInvoiceAdjustmentDto(
                r.BenefitType,
                r.Code,
                r.CodeReservationId,
                r.DiscountCents
            ))
            .ToList();

        bus.TenantId = PlatformTenant.Id.ToString();
        await bus.PublishAsync(
            new OnboardingInvoiceRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                CorrelationId = correlation.CorrelationId,
                OnboardingId = cmd.OnboardingId,
                PlanId = cmd.PlanId,
                PlanDescription = cmd.PlanDescription,
                PayerName = cmd.PayerName,
                PayerEmail = cmd.PayerEmail,
                PaymentId = cmd.PaymentId,
                GrossAmountCents = cmd.GrossAmountCents,
                DiscountAmountCents = cmd.DiscountAmountCents,
                NetAmountCents = cmd.NetAmountCents,
                Currency = cmd.Currency,
                SettlementType = cmd.SettlementType,
                Adjustments = adjustments,
            }
        );

        logger.LogInformation(
            "Onboarding {OnboardingId} finalized ({Settlement}, net {Net} {Currency}, {Codes} code(s)).",
            cmd.OnboardingId,
            cmd.SettlementType,
            cmd.NetAmountCents,
            cmd.Currency,
            cmd.Reservations.Count
        );
        return Result.Success();
    }
}
