using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>Rate limiting fail-closed para la creación y el reenvío de OTPs de onboarding.</summary>
public interface IOnboardingOtpThrottler
{
    Task<Result> AuthorizeChallengeCreationAsync(string email, string ipAddress, CancellationToken ct = default);
    Task<Result> AuthorizeResendAsync(Guid challengeId, CancellationToken ct = default);
}
