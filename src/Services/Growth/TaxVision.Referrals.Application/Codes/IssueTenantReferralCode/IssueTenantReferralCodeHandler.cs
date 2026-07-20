using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;

public static class IssueTenantReferralCodeHandler
{
    private const string Operation = "Referrals.IssueTenantReferralCode.v1";
    private const int DisplayPrefixLength = 8;

    public static async Task<Result<IssueTenantReferralCodeResult>> Handle(
        IssueTenantReferralCodeCommand command,
        IReferralProgramRepository programs,
        IReferralCodeRepository codes,
        IReferralCodeTokenGenerator tokenGenerator,
        IReferralCodeTokenHasher tokenHasher,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<IssueTenantReferralCodeResult>(actor.Error);

        if (command.TenantId == Guid.Empty || command.ProgramId == Guid.Empty)
        {
            return Failure(
                "ReferralCode.InvalidScope",
                "TenantId and ProgramId are required."
            );
        }

        if (command.ExpiresAtUtc.Kind != DateTimeKind.Utc)
        {
            return Failure(
                "ReferralCode.InvalidUtcExpiry",
                "ExpiresAtUtc must use DateTimeKind.Utc."
            );
        }

        var generated = tokenGenerator.Generate(
            command.ProgramId,
            command.TenantId,
            command.IdempotencyKey
        );
        if (generated.IsFailure)
            return Result.Failure<IssueTenantReferralCodeResult>(generated.Error);

        var clearText = generated.Value.Reveal();
        var codeHash = tokenHasher.Hash(clearText);
        if (codeHash.IsFailure)
            return Result.Failure<IssueTenantReferralCodeResult>(codeHash.Error);

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.TenantId,
            command.ProgramId,
            command.ExpiresAtUtc,
            codeHash.Value,
            command.ActorUserId
        );

        return await idempotency.ExecuteAsync(
            Operation,
            command.ProgramId,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var program = await programs.GetForEvaluationAsync(
                    command.ProgramId,
                    operationCt
                );
                if (
                    program is null
                    || program.ScopeType != ReferralProgramScope.Platform
                    || program.FlowType != ReferralFlowType.TenantToTenant
                )
                {
                    return Failure(
                        "ReferralProgram.NotFound",
                        "An applicable tenant-to-tenant referral program was not found."
                    );
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var accepting = program.EnsureAcceptingAttributions(nowUtc);
                if (accepting.IsFailure)
                    return Result.Failure<IssueTenantReferralCodeResult>(accepting.Error);

                var existing = await codes.GetActiveOwnedAsync(
                    command.TenantId,
                    command.ProgramId,
                    ReferralParticipantType.Tenant,
                    command.TenantId,
                    operationCt
                );
                if (existing is not null)
                {
                    return Failure(
                        "ReferralCode.ActiveOwnerExists",
                        "The tenant already has an active code for this referral program."
                    );
                }

                var created = ReferralCode.Create(
                    program,
                    ReferralParticipantType.Tenant,
                    command.TenantId,
                    codeHash.Value,
                    clearText[..Math.Min(DisplayPrefixLength, clearText.Length)],
                    clearText[^4..],
                    command.ExpiresAtUtc,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc
                );
                if (created.IsFailure)
                    return Result.Failure<IssueTenantReferralCodeResult>(created.Error);

                await codes.AddAsync(created.Value, operationCt);
                return Result.Success(IssueTenantReferralCodeResult.From(created.Value));
            },
            ct
        );
    }

    private static Result<IssueTenantReferralCodeResult> Failure(
        string code,
        string message
    ) => Result.Failure<IssueTenantReferralCodeResult>(new Error(code, message));
}
