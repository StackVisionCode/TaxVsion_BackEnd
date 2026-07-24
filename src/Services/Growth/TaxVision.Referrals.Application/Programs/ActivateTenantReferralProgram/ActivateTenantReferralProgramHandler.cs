using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Application.Programs.Common;

namespace TaxVision.Referrals.Application.Programs.ActivateTenantReferralProgram;

public static class ActivateTenantReferralProgramHandler
{
    private const string Operation = "Referrals.ActivateTenantReferralProgram.v1";

    public static async Task<Result<TenantReferralProgramResult>> Handle(
        ActivateTenantReferralProgramCommand command,
        IReferralProgramRepository programs,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<TenantReferralProgramResult>(actor.Error);

        if (command.ProgramId == Guid.Empty)
        {
            return Failure("ReferralProgram.InvalidProgram", "ProgramId is required.");
        }

        var fingerprint = CanonicalPayloadFingerprint.Compute(command.ProgramId, command.ActorUserId);

        // Los programs T2T son platform-owned (ver GetOwnedByIdAsync abajo con PlatformTenant.Id).
        return await idempotency.ExecuteAsync(
            PlatformTenant.Id,
            Operation,
            command.ProgramId,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var program = await programs.GetOwnedByIdAsync(PlatformTenant.Id, command.ProgramId, operationCt);
                if (program is null)
                {
                    return Failure("ReferralProgram.NotFound", "The platform-owned referral program was not found.");
                }

                var activated = program.Activate(command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
                return activated.IsFailure
                    ? Result.Failure<TenantReferralProgramResult>(activated.Error)
                    : Result.Success(TenantReferralProgramResult.From(program));
            },
            ct
        );
    }

    private static Result<TenantReferralProgramResult> Failure(string code, string message) =>
        Result.Failure<TenantReferralProgramResult>(new Error(code, message));
}
