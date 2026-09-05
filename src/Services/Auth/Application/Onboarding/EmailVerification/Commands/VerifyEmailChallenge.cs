using System.Text.Json.Serialization;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Sessions;

namespace TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;

public sealed record VerifyEmailChallengeCommand(Guid ChallengeId, string Code);

public sealed record VerifyEmailChallengeResponse(
    [property: JsonIgnore] string SessionToken,
    DateTime ExpiresAtUtc,
    string TokenType
);

public static class VerifyEmailChallengeHandler
{
    public static async Task<Result<VerifyEmailChallengeResponse>> Handle(
        VerifyEmailChallengeCommand command,
        IEmailVerificationChallengeRepository challenges,
        IUnitOfWork unitOfWork,
        OnboardingSessionService sessions,
        CancellationToken ct
    )
    {
        var challenge = await challenges.GetByIdAsync(command.ChallengeId, ct);
        if (challenge is null)
        {
            return Result.Failure<VerifyEmailChallengeResponse>(
                new Error("Onboarding.ChallengeNotFound", "The verification challenge was not found.")
            );
        }

        var nowUtc = DateTime.UtcNow;
        var result = challenge.Verify(command.Code, nowUtc);

        // Persist failed attempts before issuing any bearer credential.
        await unitOfWork.SaveChangesAsync(ct);
        if (result.IsFailure)
            return Result.Failure<VerifyEmailChallengeResponse>(result.Error);

        var session = await sessions.IssueAsync(challenge.Id, challenge.Email, nowUtc, ct);
        if (session.IsFailure)
            return Result.Failure<VerifyEmailChallengeResponse>(session.Error);

        return Result.Success(
            new VerifyEmailChallengeResponse(
                session.Value.SessionToken,
                session.Value.ExpiresAtUtc,
                session.Value.TokenType
            )
        );
    }
}
