using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;

public sealed record VerifyEmailChallengeCommand(Guid ChallengeId, string Code);

public static class VerifyEmailChallengeHandler
{
    public static async Task<Result> Handle(
        VerifyEmailChallengeCommand command,
        IEmailVerificationChallengeRepository challenges,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var challenge = await challenges.GetByIdAsync(command.ChallengeId, ct);
        if (challenge is null)
            return Result.Failure(
                new Error("Onboarding.ChallengeNotFound", "The verification challenge was not found.")
            );

        var result = challenge.Verify(command.Code, DateTime.UtcNow);

        // Persistimos siempre — incluso en fallo hay que guardar el incremento de Attempts.
        await unitOfWork.SaveChangesAsync(ct);
        return result;
    }
}
