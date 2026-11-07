using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Onboarding.Application.EmailVerification.Commands;

public sealed record ResendEmailChallengeCommand(Guid ChallengeId);

public static class ResendEmailChallengeHandler
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public static async Task<Result> Handle(
        ResendEmailChallengeCommand command,
        IEmailVerificationChallengeRepository challenges,
        ILoginThrottler throttler,
        IOtpCodeGenerator otpGenerator,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var challenge = await challenges.GetByIdAsync(command.ChallengeId, ct);
        if (challenge is null)
            return Result.Failure(
                new Error("Onboarding.ChallengeNotFound", "The verification challenge was not found.")
            );

        var throttleResult = await throttler.AuthorizeOnboardingResendAsync(command.ChallengeId, ct);
        if (throttleResult.IsFailure)
            return throttleResult;

        var now = DateTime.UtcNow;
        var otpCode = otpGenerator.Generate();
        var result = challenge.Resend(otpCode, now, Ttl);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new OnboardingOtpRequestedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                ChallengeId = challenge.Id,
                Email = challenge.Email,
                OtpCode = otpCode,
                ExpiresAtUtc = challenge.ExpiresAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );
        return Result.Success();
    }
}
