using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Participants;

namespace TaxVision.Referrals.Application.Codes.GetMyReferralCode;

public static class GetMyReferralCodeHandler
{
    public static async Task<Result<MyReferralCodeResult>> Handle(
        GetMyReferralCodeQuery query,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (query.TenantId == Guid.Empty)
        {
            return Result.Failure<MyReferralCodeResult>(
                new Error("ReferralCode.InvalidScope", "TenantId is required.")
            );
        }

        var program = await programs.GetActivePlatformProgramAsync(ct);
        if (program is null)
        {
            return Result.Failure<MyReferralCodeResult>(
                new Error("ReferralProgram.NotFound", "There is no active referral program.")
            );
        }

        var code = await codes.GetActiveOwnedAsync(
            query.TenantId,
            program.Id,
            ReferralParticipantType.Tenant,
            query.TenantId,
            ct
        );
        if (code is null || code.EnsureUsable(timeProvider.GetUtcNow().UtcDateTime).IsFailure)
        {
            return Result.Failure<MyReferralCodeResult>(
                new Error("ReferralCode.NotFound", "The office has no active referral code yet.")
            );
        }

        return Result.Success(
            new MyReferralCodeResult(
                code.Id,
                code.ProgramId,
                code.Status,
                code.DisplayPrefix,
                code.LastFour,
                code.ExpiresAtUtc,
                code.IdempotencyKey
            )
        );
    }
}
