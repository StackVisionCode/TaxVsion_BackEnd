using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record ReactivateExpiredOnboardingAdminCommand(Guid OnboardingId, string Reason, Guid AdminUserId);

/// <summary>OBF-3.1 — admin revive un onboarding Expired cuando el pago YA fue cobrado (caso Smailin:
/// pagó + volvió >13h después + el sweeper lo había marcado Expired). Pre-check estricto: Status debe ser
/// Expired y debe haber pago. Reusa el pipeline de regeneración de token + publicación compartido con
/// ResendOnboardingRegistrationEmailAdminHandler para que ambos caminos converjan en el mismo evento y la
/// misma auditoría de email.</summary>
public static class ReactivateExpiredOnboardingAdminHandler
{
    public static async Task<Result> Handle(
        ReactivateExpiredOnboardingAdminCommand command,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        ITokenReferenceStore tokenReferences,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IOptions<OnboardingOptions> onboardingOptions,
        IAuthAuditWriter audit,
        IRequestContext request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Result.Failure(new Error("OnboardingAdmin.ReasonRequired", "A reason is required."));

        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        // Pre-check específico de reactivación: solo tiene sentido sobre un Expired con pago. Resend se
        // usa para RegistrationPending/PaymentCompleted; separar los dos endpoints hace explícito al admin
        // qué está haciendo y evita que un click en Reactivate cambie el CreatedAtUtc de un onboarding sano.
        if (onboarding.Status != TenantOnboardingStatus.Expired)
            return Result.Failure(
                new Error(
                    "OnboardingAdmin.NotExpired",
                    $"Reactivate only applies to Expired onboardings; current status is '{onboarding.Status}'."
                )
            );

        var regenerate = await ResendOnboardingRegistrationEmailAdminHandler.RegenerateAndPublishAsync(
            onboarding,
            tokens,
            tokenReferences,
            planCatalog,
            bus,
            correlation,
            onboardingOptions.Value,
            ct
        );
        if (regenerate.IsFailure)
            return regenerate;

        await audit.AddAsync(
            AuthAuditLog.Record(
                onboarding.TenantId ?? Guid.Empty,
                command.AdminUserId,
                AuthAuditAction.OnboardingAdminReactivated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantOnboarding",
                targetId: onboarding.Id
            ),
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
