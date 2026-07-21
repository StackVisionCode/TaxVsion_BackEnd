using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

public static class CreateReferralAttributionHandler
{
    private const string Operation = "CreateReferralAttribution";

    public static async Task<Result<CreateReferralAttributionResult>> Handle(
        CreateReferralAttributionCommand command,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        IReferralAttributionRepository attributions,
        IReferralCodeTokenHasher codeTokenHasher,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<CreateReferralAttributionResult>(actor.Error);

        if (command.TenantId == Guid.Empty || command.RefereeId == Guid.Empty)
        {
            return Result.Failure<CreateReferralAttributionResult>(
                new Error("ReferralAttribution.InvalidParticipant", "TenantId and RefereeId are required.")
            );
        }

        var codeHash = codeTokenHasher.Hash(command.ReferralCode);
        if (codeHash.IsFailure)
            return Result.Failure<CreateReferralAttributionResult>(codeHash.Error);

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.TenantId,
            command.ProgramId,
            codeHash.Value,
            command.RefereeType,
            command.RefereeId
        );

        return await idempotency.ExecuteAsync(
            Operation,
            command.TenantId,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var program = await programs.GetForEvaluationAsync(command.ProgramId, operationCt);
                if (program is null)
                    return NotFound();

                if (
                    (program.FlowType == ReferralFlowType.TenantToTenant && command.TenantId != command.RefereeId)
                    || (
                        program.FlowType == ReferralFlowType.TaxpayerToTaxpayer
                        && program.TenantScopeId != command.TenantId
                    )
                )
                {
                    return NotFound();
                }

                var code = await codes.ResolveByHashAsync(program.Id, codeHash.Value, operationCt);
                if (code is null)
                {
                    return Result.Failure<CreateReferralAttributionResult>(
                        new Error("ReferralCode.Invalid", "The referral code is invalid or unavailable.")
                    );
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var created = ReferralAttribution.Create(
                    program,
                    code,
                    command.RefereeType,
                    command.RefereeId,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc
                );
                if (created.IsFailure)
                    return Result.Failure<CreateReferralAttributionResult>(created.Error);

                var activated = created.Value.Activate(command.ActorUserId, nowUtc);
                if (activated.IsFailure)
                    return Result.Failure<CreateReferralAttributionResult>(activated.Error);

                await attributions.AddAsync(created.Value, operationCt);
                return Result.Success(
                    new CreateReferralAttributionResult(created.Value.Id, created.Value.Status, WasReplay: false)
                );
            },
            ct
        );
    }

    private static Result<CreateReferralAttributionResult> NotFound() =>
        Result.Failure<CreateReferralAttributionResult>(
            new Error("ReferralProgram.NotFound", "The referral program does not exist.")
        );
}
