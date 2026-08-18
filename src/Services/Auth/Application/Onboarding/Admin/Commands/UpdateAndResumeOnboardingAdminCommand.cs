using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record UpdateAndResumeOnboardingAdminCommand(Guid OnboardingId, string? Subdomain, Guid? PlanId);

/// <summary>PayFlow (Fase 17) — receptor de <c>POST /auth/onboarding/admin/{id}/update-and-resume</c>.
/// Mismo flujo que <see cref="ResumeOnboardingAdminCommand"/>, pero corrige el subdominio/plan que
/// causó el fallo antes de reintentar (p.ej. subdominio ya tomado, plan despublicado).</summary>
public static class UpdateAndResumeOnboardingAdminHandler
{
    public static async Task<Result> Handle(
        UpdateAndResumeOnboardingAdminCommand command,
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

        var updateResult = onboarding.UpdateProvisioningInputs(command.Subdomain, command.PlanId);
        if (updateResult.IsFailure)
            return updateResult;

        var resetResult = onboarding.ResetRetryState();
        if (resetResult.IsFailure)
            return resetResult;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new ResumeOnboardingProvisioningCommand(command.OnboardingId));
        return Result.Success();
    }
}
