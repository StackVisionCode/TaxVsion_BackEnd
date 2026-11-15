using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;

namespace TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

/// <summary>
/// FINALIZE de la operación comercial del onboarding — se invoca vía <see cref="OnboardingFinalizeCommand"/>
/// (auto-contenido, sin recargar el aggregate): commit de TODAS las reservas de código en Growth (idempotente).
/// El documento del cobro es el recibo del pago en PaymentApp; Billing es de las facturas de la oficina a sus
/// clientes y no recibe nada de la plataforma.
/// </summary>
public sealed class OnboardingFinalizer(IGrowthOnboardingClient growth, ILogger<OnboardingFinalizer> logger)
{
    public async Task<Result> FinalizeAsync(OnboardingFinalizeCommand cmd, CancellationToken ct)
    {
        // El OnboardingId es un source-event estable para el commit/qualify (idempotencia + fingerprint).
        var sourceEventId = cmd.OnboardingId;

        // Commit de todas las reservas (idempotente por reserva). Un fallo se propaga → Wolverine reintenta,
        // salvo el commit tardío que Growth dejó en revisión: no se reintenta y no corta el alta.
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
            if (commit.IsFailure && commit.Error.Code == GrowthOnboardingErrors.LateCommitPendingReview)
            {
                logger.LogWarning(
                    "Code reservation {ReservationId} of onboarding {OnboardingId} is pending review in Growth (late commit); onboarding not blocked.",
                    reservation.CodeReservationId,
                    cmd.OnboardingId
                );
                continue;
            }
            if (commit.IsFailure)
                return commit;
        }

        // El referido se califica en Growth al llegar el evento del pago del alta (sin llamada M2M de Auth).
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
