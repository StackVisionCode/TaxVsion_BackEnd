using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>
/// Libera (cancela) INMEDIATAMENTE en Growth todas las reservas de código de un onboarding cuando el
/// proceso se cancela de forma EXPLÍCITA (el comprador cancela el checkout, o un admin cancela el
/// onboarding). Complementa al sweeper de vencimiento de Growth (red de seguridad para el abandono
/// silencioso): acá la señal de cancelación es determinística, así que no hay riesgo de liberar un
/// código mientras un pago reintenta. Cada cancel usa la MISMA referencia de pago por orden que la
/// reserva/commit (<see cref="OnboardingPaymentReference"/>) — Growth valida que coincida. Idempotente
/// (Growth acepta cancelar una reserva ya cancelada/expirada); un fallo individual no aborta el resto.
/// </summary>
public sealed class OnboardingReservationCanceller(
    IGrowthOnboardingClient growth,
    ILogger<OnboardingReservationCanceller> logger
)
{
    public async Task CancelAllAsync(TenantOnboarding onboarding, string reason, CancellationToken ct)
    {
        foreach (var reservation in onboarding.CodeReservations)
        {
            var result = await growth.CancelAsync(
                reservation.CodeReservationId,
                OnboardingPaymentReference.For(onboarding.Id, reservation.Order),
                reason,
                idempotencyKey: $"onb-cancel:{onboarding.Id:N}:{reservation.CodeReservationId:N}",
                ct
            );

            // No bloquea la cancelación del onboarding: si Growth falla, el sweeper libera al vencer el TTL.
            if (result.IsFailure)
                logger.LogWarning(
                    "Failed to release code reservation {ReservationId} for cancelled onboarding {OnboardingId}: {Code}.",
                    reservation.CodeReservationId,
                    onboarding.Id,
                    result.Error.Code
                );
        }
    }
}
