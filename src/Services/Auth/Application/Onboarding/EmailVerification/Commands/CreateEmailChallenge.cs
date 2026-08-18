using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;

public sealed record CreateEmailChallengeCommand(string Email, string IpAddress, string? FirstNameHint = null);

public static class CreateEmailChallengeHandler
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public static async Task<Result<Guid>> Handle(
        CreateEmailChallengeCommand command,
        IEmailVerificationChallengeRepository challenges,
        ILoginThrottler throttler,
        IOtpCodeGenerator otpGenerator,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var throttleResult = await throttler.AuthorizeOnboardingChallengeCreationAsync(
            command.Email,
            command.IpAddress,
            ct
        );
        if (throttleResult.IsFailure)
            return Result.Failure<Guid>(throttleResult.Error);

        var now = DateTime.UtcNow;
        var otpCode = otpGenerator.Generate();
        var challengeResult = EmailVerificationChallenge.Create(command.Email, otpCode, now, Ttl);
        if (challengeResult.IsFailure)
            return Result.Failure<Guid>(challengeResult.Error);

        var challenge = challengeResult.Value;
        await challenges.AddAsync(challenge, ct);

        await bus.PublishAsync(
            new OnboardingOtpRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                ChallengeId = challenge.Id,
                Email = challenge.Email,
                OtpCode = otpCode,
                ExpiresAtUtc = challenge.ExpiresAtUtc,
                FirstNameHint = command.FirstNameHint,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(challenge.Id);
    }
}
