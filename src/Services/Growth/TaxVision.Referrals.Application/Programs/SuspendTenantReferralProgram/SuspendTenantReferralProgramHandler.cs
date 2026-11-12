using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Application.Programs.Common;

namespace TaxVision.Referrals.Application.Programs.SuspendTenantReferralProgram;

/// <summary>
/// Suspende el programa: deja de aceptar códigos y atribuciones nuevos. Los premios ya solicitados
/// y las atribuciones existentes no se tocan (cada premio congela su propio snapshot).
/// </summary>
public static class SuspendTenantReferralProgramHandler
{
    private const string Operation = "Referrals.SuspendTenantReferralProgram.v1";

    public static async Task<Result<TenantReferralProgramResult>> Handle(
        SuspendTenantReferralProgramCommand command,
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
            return Result.Failure<TenantReferralProgramResult>(
                new Error("ReferralProgram.InvalidProgram", "ProgramId is required.")
            );
        }

        var fingerprint = CanonicalPayloadFingerprint.Compute(command.ProgramId, command.ActorUserId);

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
                    return Result.Failure<TenantReferralProgramResult>(
                        new Error("ReferralProgram.NotFound", "The platform-owned referral program was not found.")
                    );
                }

                var suspended = program.Suspend(command.ActorUserId, timeProvider.GetUtcNow().UtcDateTime);
                return suspended.IsFailure
                    ? Result.Failure<TenantReferralProgramResult>(suspended.Error)
                    : Result.Success(TenantReferralProgramResult.From(program));
            },
            ct
        );
    }
}
