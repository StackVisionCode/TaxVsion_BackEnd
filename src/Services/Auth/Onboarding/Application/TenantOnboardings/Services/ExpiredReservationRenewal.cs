using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Commands;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

/// <summary>
/// La reserva del código en Growth vive 24 h, pero el pago se puede reintentar más tiempo. Al reintentar con una
/// reserva vencida se reservan de nuevo los MISMOS códigos con referencias de pago nuevas; el pago ya tiene fijado el
/// neto, así que solo sirve un descuento idéntico. Si no, no se cobra: <c>Onboarding.CodeNoLongerAvailable</c>.
/// </summary>
public static class ExpiredReservationRenewal
{
    public static readonly Error CodeNoLongerAvailable = new(
        "Onboarding.CodeNoLongerAvailable",
        "The discount code is no longer available. Start your registration again to continue."
    );

    public static async Task<Result> RenewAsync(
        TenantOnboarding onboarding,
        OnboardingCodeReserver reserver,
        IGrowthOnboardingClient growth,
        ILogger<StartOnboardingCheckoutCommand> logger,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        if (
            onboarding.Status != TenantOnboardingStatus.PendingPayment
            || !onboarding.HasExpiredCodeReservations(nowUtc)
        )
            return Result.Success();

        var previous = onboarding.CodeReservations.OrderBy(r => r.Order).ToList();
        // Reservas sin código visible (descuento automático antiguo): no se pueden volver a reservar.
        if (previous.Any(r => string.IsNullOrWhiteSpace(r.Code)))
            return Result.Failure(CodeNoLongerAvailable);

        var codes = previous.Select(r => new OnboardingCodeInput(r.Code!, r.BenefitType, r.Code)).ToList();
        var reserved = await reserver.ReserveAsync(onboarding, codes, ct, onboarding.PricingGeneration + 1);
        if (reserved.IsFailure)
        {
            logger.LogWarning(
                "Onboarding {OnboardingId}: expired code reservation could not be renewed ({Code}).",
                onboarding.Id,
                reserved.Error.Code
            );
            return Result.Failure(CodeNoLongerAvailable);
        }

        var renewed = onboarding.RenewCodeReservations(reserved.Value.Reservations, nowUtc);
        if (renewed.IsFailure)
        {
            // La reserva nueva no sirve (el descuento cambió): se libera para no retener el código.
            await ReleaseAsync(onboarding.Id, reserved.Value.Reservations, growth, logger, ct);
            return renewed;
        }

        await ReleaseAsync(onboarding.Id, previous.Select(ToInput).ToList(), growth, logger, ct);
        return Result.Success();
    }

    private static OnboardingCodeReservationInput ToInput(OnboardingCodeReservation r) =>
        new(
            r.CodeReservationId,
            r.BenefitType,
            r.Code,
            r.DiscountCents,
            r.SnapshotHash,
            r.ExpiresAtUtc,
            r.PaymentReferenceId ?? OnboardingPaymentReference.For(r.OnboardingId, r.Order)
        );

    // Best-effort: si Growth falla, su job de expiración las libera igual.
    private static async Task ReleaseAsync(
        Guid onboardingId,
        IReadOnlyList<OnboardingCodeReservationInput> reservations,
        IGrowthOnboardingClient growth,
        ILogger logger,
        CancellationToken ct
    )
    {
        foreach (var reservation in reservations)
        {
            if (reservation.PaymentReferenceId is not { } paymentReferenceId)
                continue;

            var cancelled = await growth.CancelAsync(
                reservation.CodeReservationId,
                paymentReferenceId,
                "Onboarding code reservation renewed",
                idempotencyKey: $"onb-renew-cancel:{onboardingId:N}:{reservation.CodeReservationId:N}",
                ct
            );
            if (cancelled.IsFailure)
                logger.LogWarning(
                    "Could not release code reservation {ReservationId} of onboarding {OnboardingId}: {Code}.",
                    reservation.CodeReservationId,
                    onboardingId,
                    cancelled.Error.Code
                );
        }
    }
}
