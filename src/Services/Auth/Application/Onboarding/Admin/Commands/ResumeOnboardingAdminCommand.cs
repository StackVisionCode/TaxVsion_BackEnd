using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record ResumeOnboardingAdminCommand(Guid OnboardingId);

/// <summary>PayFlow (Fase 17) — receptor de <c>POST /auth/onboarding/admin/{id}/resume</c>. Solo la
/// Saga (<see cref="ResumeOnboardingProvisioningCommand"/>) tiene el estado en memoria
/// (Email/OfficeName/etc) para reconstruir el comando M2M exacto — este handler solo valida,
/// resetea el conteo de reintentos (fresh start real de un admin) y despacha.</summary>
public static class ResumeOnboardingAdminHandler
{
    public static async Task<Result> Handle(
        ResumeOnboardingAdminCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        if (onboarding.FailedStep is not (TenantProvisioningStep.Tenant or TenantProvisioningStep.Subscription))
        {
            return Result.Failure(
                new Error(
                    "Onboarding.NotResumable",
                    "This onboarding's failed step cannot be auto-resumed (e.g. TenantAdmin — password reference is single-use). Use force-complete or cancel-and-refund instead."
                )
            );
        }

        var resetResult = onboarding.ResetRetryState();
        if (resetResult.IsFailure)
            return resetResult;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new ResumeOnboardingProvisioningCommand(command.OnboardingId));
        return Result.Success();
    }
}
