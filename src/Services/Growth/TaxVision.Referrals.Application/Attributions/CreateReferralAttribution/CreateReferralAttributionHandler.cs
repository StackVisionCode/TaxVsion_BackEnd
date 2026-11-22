using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

public static class CreateReferralAttributionHandler
{
    private const string Operation = "CreateReferralAttribution";

    /// <summary>
    /// Devuelve el resultado y, si el comando lo pide, el aviso al referidor como mensaje en cascada: Wolverine lo
    /// publica por el outbox de este mismo mensaje. Solo nace en la ejecución real; una repetición idempotente no
    /// corre el cuerpo y no lo repite.
    /// </summary>
    public static async Task<(Result<CreateReferralAttributionResult> Result, object? Notice)> Handle(
        CreateReferralAttributionCommand command,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        IReferralAttributionRepository attributions,
        IReferralCodeTokenHasher codeTokenHasher,
        IReferralIdempotencyExecutor idempotency,
        IReferralCodeShareRepository shares,
        IReferralNoticeEvents noticeEvents,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        object? notice = null;
        var result = await CreateAsync(
            command,
            programs,
            codes,
            attributions,
            codeTokenHasher,
            idempotency,
            shares,
            timeProvider,
            created =>
                notice =
                    command.NoticeCorrelationId is { } correlationId && created.ReferrerTenantId != Guid.Empty
                        ? noticeEvents.Attributed(created, correlationId)
                        : null,
            ct
        );
        return (result, result.IsSuccess ? notice : null);
    }

    private static async Task<Result<CreateReferralAttributionResult>> CreateAsync(
        CreateReferralAttributionCommand command,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        IReferralAttributionRepository attributions,
        IReferralCodeTokenHasher codeTokenHasher,
        IReferralIdempotencyExecutor idempotency,
        IReferralCodeShareRepository shares,
        TimeProvider timeProvider,
        Action<CreateReferralAttributionResult> onCreated,
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
            command.TenantId,
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

                // El envío viaja en la URL: solo cuenta si es de este mismo código.
                var shareId =
                    command.ShareId is { } requested
                    && await shares.FindReferralCodeIdAsync(requested, operationCt) == code.Id
                        ? requested
                        : (Guid?)null;

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var created = ReferralAttribution.Create(
                    program,
                    code,
                    command.RefereeType,
                    command.RefereeId,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc,
                    command.Channel,
                    shareId
                );
                if (created.IsFailure)
                    return Result.Failure<CreateReferralAttributionResult>(created.Error);

                var activated = created.Value.Activate(command.ActorUserId, nowUtc);
                if (activated.IsFailure)
                    return Result.Failure<CreateReferralAttributionResult>(activated.Error);

                await attributions.AddAsync(created.Value, operationCt);
                var attributed = new CreateReferralAttributionResult(
                    created.Value.Id,
                    created.Value.Status,
                    WasReplay: false,
                    code.OwnerId,
                    code.OwnerUserId,
                    command.Channel?.ToString()
                );
                onCreated(attributed);
                return Result.Success(attributed);
            },
            ct
        );
    }

    private static Result<CreateReferralAttributionResult> NotFound() =>
        Result.Failure<CreateReferralAttributionResult>(
            new Error("ReferralProgram.NotFound", "The referral program does not exist.")
        );
}
