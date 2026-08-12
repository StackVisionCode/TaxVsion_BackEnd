using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

/// <summary>
/// Cancelación EXPLÍCITA del onboarding por el comprador (vuelve de Stripe con checkout cancelado, o
/// abandona a propósito). Libera al INSTANTE las reservas de código en Growth (no espera al sweeper de
/// 24h) y deja el onboarding en <c>Cancelled</c>. Anónimo (pre-tenant, sin sesión). Idempotente.
/// </summary>
public sealed record CancelOnboardingCommand(Guid OnboardingId, string? Reason = null);

public static class CancelOnboardingHandler
{
    public static async Task<Result> Handle(
        CancelOnboardingCommand command,
        ITenantOnboardingRepository onboardings,
        OnboardingReservationCanceller canceller,
        IUnitOfWork unitOfWork,
        ILogger<CancelOnboardingCommand> logger,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        if (onboarding.Status == TenantOnboardingStatus.Cancelled)
            return Result.Success(); // idempotente

        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? "Buyer cancelled the checkout."
            : command.Reason.Trim();

        // Cancel() valida el estado: rechaza si ya está pagado/provisionado (no se libera un código de un
        // onboarding que ya completó). Recién si la transición es válida liberamos las reservas.
        var cancelResult = onboarding.Cancel(reason);
        if (cancelResult.IsFailure)
            return cancelResult;

        await canceller.CancelAllAsync(onboarding, reason, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Onboarding {OnboardingId} cancelled by buyer; released {Count} code reservation(s).",
            onboarding.Id,
            onboarding.CodeReservations.Count
        );
        return Result.Success();
    }
}
