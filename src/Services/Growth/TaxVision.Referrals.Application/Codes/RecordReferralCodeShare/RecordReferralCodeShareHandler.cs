using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Codes.RecordReferralCodeShare;

public static class RecordReferralCodeShareHandler
{
    public static async Task<Result<ReferralCodeShareResult>> Handle(
        RecordReferralCodeShareCommand command,
        IReferralCodeRepository codes,
        IReferralCodeShareRepository shares,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var code = await codes.GetOwnedByIdAsync(command.TenantId, command.ReferralCodeId, ct);
        if (code is null)
        {
            return Result.Failure<ReferralCodeShareResult>(
                new Error("ReferralCode.NotFound", "The referral code does not exist.")
            );
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var share = ReferralCodeShare.Record(code, command.Channel, command.UserId, command.RecipientHint, nowUtc);
        if (share.IsFailure)
            return Result.Failure<ReferralCodeShareResult>(share.Error);

        await shares.AddAsync(share.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new ReferralCodeShareResult(share.Value.Id, code.Id, share.Value.Channel, share.Value.SharedAtUtc)
        );
    }
}
