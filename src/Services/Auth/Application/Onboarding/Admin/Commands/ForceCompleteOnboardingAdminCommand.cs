using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record ForceCompleteOnboardingAdminCommand(Guid OnboardingId, string Reason);

/// <summary>PayFlow (Fase 17) — receptor de <c>POST /auth/onboarding/admin/{id}/force-complete</c>.
/// Cierre administrativo excepcional — ver doc-comment de <c>TenantOnboarding.AdminForceComplete</c>
/// para las precondiciones (Tenant/TenantAdmin/Subscription deben existir).</summary>
public static class ForceCompleteOnboardingAdminHandler
{
    public static async Task<Result> Handle(
        ForceCompleteOnboardingAdminCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Result.Failure(new Error("Onboarding.ForceCompleteReasonRequired", "A reason is required."));

        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        var result = onboarding.AdminForceComplete(command.Reason, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
