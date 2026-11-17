using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Referrals.Application.Attributions.OnboardingReferralRejections;

/// <summary>Anota que la declaración de referido del alta se descartó. Idempotente por alta. El alta debe estar en
/// contexto como tenant.</summary>
public sealed record RecordOnboardingReferralRejectionCommand(Guid OnboardingId, string ReasonCode);

/// <summary>¿La declaración de referido del alta se descartó? El alta debe estar en contexto como tenant.</summary>
public sealed record HasOnboardingReferralRejectionQuery(Guid OnboardingId);

public static class RecordOnboardingReferralRejectionHandler
{
    public static async Task<Result> Handle(
        RecordOnboardingReferralRejectionCommand command,
        IOnboardingReferralRejectionRepository rejections,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (await rejections.ExistsAsync(command.OnboardingId, ct))
            return Result.Success();

        var rejection = OnboardingReferralRejection.Create(
            command.OnboardingId,
            command.ReasonCode,
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (rejection.IsFailure)
            return Result.Failure(rejection.Error);

        await rejections.AddAsync(rejection.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class HasOnboardingReferralRejectionHandler
{
    public static async Task<Result<bool>> Handle(
        HasOnboardingReferralRejectionQuery query,
        IOnboardingReferralRejectionRepository rejections,
        CancellationToken ct
    ) =>
        query.OnboardingId == Guid.Empty
            ? Result.Failure<bool>(
                new Error("OnboardingReferralRejection.InvalidOnboarding", "An onboarding is required.")
            )
            : Result.Success(await rejections.ExistsAsync(query.OnboardingId, ct));
}
