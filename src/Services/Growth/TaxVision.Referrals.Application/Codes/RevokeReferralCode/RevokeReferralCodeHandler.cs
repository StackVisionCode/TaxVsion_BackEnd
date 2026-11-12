using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Codes;

namespace TaxVision.Referrals.Application.Codes.RevokeReferralCode;

/// <summary>
/// El PlatformAdmin revoca el código de una oficina (p. ej. por abuso) SIN reemplazarlo: deja de validar y de atribuir.
/// Los referidos ya atribuidos conservan su premio. El tenant dueño debe estar en contexto (lo fija el controlador).
/// </summary>
public sealed record RevokeReferralCodeCommand(Guid OwnerTenantId, Guid CodeId, string Reason, Guid ActorUserId);

public sealed record RevokeReferralCodeResult(Guid CodeId, ReferralCodeStatus Status);

public static class RevokeReferralCodeHandler
{
    private const int MaxReasonLength = 500;

    public static async Task<Result<RevokeReferralCodeResult>> Handle(
        RevokeReferralCodeCommand command,
        IReferralCodeRepository codes,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<RevokeReferralCodeResult>(actor.Error);

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<RevokeReferralCodeResult>(
                new Error("ReferralCode.InvalidReason", $"A reason of up to {MaxReasonLength} characters is required.")
            );
        }

        var code = await codes.GetOwnedByIdAsync(command.OwnerTenantId, command.CodeId, ct);
        if (code is null)
        {
            return Result.Failure<RevokeReferralCodeResult>(
                new Error("ReferralCode.NotFound", "The referral code does not exist.")
            );
        }

        var revoked = code.Revoke(reason, command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
        if (revoked.IsFailure)
            return Result.Failure<RevokeReferralCodeResult>(revoked.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new RevokeReferralCodeResult(code.Id, code.Status));
    }
}
