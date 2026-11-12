using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;

public static class GetActiveReferralProgramHandler
{
    public static async Task<Result<ReferralProgramTermsResult>> Handle(
        GetActiveReferralProgramQuery query,
        IReferralProgramRepository programs,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var program = await programs.GetActivePlatformProgramAsync(ct);

        // Activo pero fuera de su ventana (aún no empieza / ya terminó) no acepta referidos: se reporta
        // como no disponible, igual que si no existiera.
        if (program is null || program.EnsureAcceptingAttributions(timeProvider.GetUtcNow().UtcDateTime).IsFailure)
        {
            return Result.Failure<ReferralProgramTermsResult>(
                new Error("ReferralProgram.NotFound", "There is no active referral program.")
            );
        }

        var policy = program.Policy;
        return Result.Success(
            new ReferralProgramTermsResult(
                program.Id,
                program.Name,
                program.PolicyVersion,
                policy.RewardType,
                policy.RewardBasisPoints,
                policy.RewardCapCents,
                policy.RewardCurrency,
                policy.WaitingPeriodDays,
                policy.MaximumRewardsPerReferrerPerCalendarYear,
                policy.AttributionWindowDays
            )
        );
    }
}
