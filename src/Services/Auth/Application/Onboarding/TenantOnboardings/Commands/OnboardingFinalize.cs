using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

/// <summary>
/// Comando local (Wolverine) que ejecuta el FINALIZE de la operación comercial del onboarding: commit de
/// las reservas de código en Growth + qualify del referido (si aplica) + pedir a Billing asentar la
/// factura. Es AUTO-CONTENIDO — lleva el desglose y las reservas en el propio mensaje (tomados del
/// onboarding en memoria por el caller) — para NO depender de recargar el aggregate: el comando se
/// publica dentro de la transacción del éxito y puede procesarse antes de que otro contexto vea el
/// commit; recargar leería datos viejos (bug real: FINALIZE veía "0 code(s)"). Idempotente en Growth
/// (commit por reserva) y en Billing (factura por OnboardingId).
/// </summary>
public sealed record OnboardingFinalizeCommand(
    Guid OnboardingId,
    Guid PlanId,
    string PlanDescription,
    string PayerName,
    string? PayerEmail,
    Guid? PaymentId,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    string SettlementType,
    Guid? ReferralAttributionId,
    IReadOnlyList<FinalizeReservationDto> Reservations
);

/// <summary>Una reserva de código a commitear en Growth + su descuento (para la línea de ajuste).</summary>
public sealed record FinalizeReservationDto(
    Guid CodeReservationId,
    string BenefitType,
    string? Code,
    long DiscountCents,
    string SnapshotHash,
    // Posición de aplicación (0-based) — deriva el PaymentId único de la reserva en Growth para el commit.
    int Order
);

public static class OnboardingFinalizeHandler
{
    public static async Task Handle(
        OnboardingFinalizeCommand command,
        OnboardingFinalizer finalizer,
        ILogger<OnboardingFinalizeCommand> logger,
        CancellationToken ct
    )
    {
        var result = await finalizer.FinalizeAsync(command, ct);
        // Un fallo (Growth caído / commit fallido) lanza → Wolverine reintenta; el FINALIZE es idempotente.
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Onboarding finalize failed for {command.OnboardingId}: {result.Error.Code} - {result.Error.Message}"
            );
    }
}
