using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

public sealed record CreateOnboardingCommand(
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    Guid PlanId,
    Guid EmailVerificationChallengeId,
    string? BillingCycle = null
);

public sealed record CreateOnboardingResponse(Guid OnboardingId, string Email, Guid PlanId);

/// <summary>
/// PayFlow (Fase 9) — UoW #1 del plan: persiste el comprador + plan elegido, ANTES de crear el
/// checkout. Requiere prueba de que el email ya fue verificado por OTP (Fase 5) — el caller pasa
/// el <see cref="CreateOnboardingCommand.EmailVerificationChallengeId"/> que obtuvo al verificar,
/// en vez de que este handler intente resolver "el challenge verificado más reciente para este
/// email" (esa consulta no existe hoy en <see cref="IEmailVerificationChallengeRepository"/> y
/// agregarla sería una superficie nueva no pedida por esta fase).
/// </summary>
public static class CreateOnboardingHandler
{
    public static async Task<Result<CreateOnboardingResponse>> Handle(
        CreateOnboardingCommand command,
        IEmailVerificationChallengeRepository challenges,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        IOnboardingMetrics metrics,
        CancellationToken ct
    )
    {
        var challenge = await challenges.GetByIdAsync(command.EmailVerificationChallengeId, ct);
        if (challenge is null)
            return Result.Failure<CreateOnboardingResponse>(
                new Error("Onboarding.ChallengeNotFound", "Email verification challenge not found.")
            );

        if (!string.Equals(challenge.Email, command.Email, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<CreateOnboardingResponse>(
                new Error("Onboarding.ChallengeEmailMismatch", "The verification challenge does not match this email.")
            );

        if (challenge.VerifiedAtUtc is null)
            return Result.Failure<CreateOnboardingResponse>(
                new Error("Onboarding.EmailNotVerified", "This email has not been verified yet.")
            );

        var nowUtc = DateTime.UtcNow;
        var result = TenantOnboarding.Create(
            command.Email,
            challenge.VerifiedAtUtc.Value,
            command.PlanId,
            command.FirstName,
            command.LastName,
            command.Phone,
            nowUtc,
            command.BillingCycle
        );
        if (result.IsFailure)
            return Result.Failure<CreateOnboardingResponse>(result.Error);

        var onboarding = result.Value;
        await onboardings.AddAsync(onboarding, ct);
        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordStarted();

        return Result.Success(new CreateOnboardingResponse(onboarding.Id, onboarding.Email, onboarding.PlanId));
    }
}
