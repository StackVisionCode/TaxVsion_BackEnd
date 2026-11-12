using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Application.Programs.Common;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Programs.UpdateTenantReferralProgramPolicy;

/// <summary>
/// Nueva versión de la política (p. ej. cambiar el % del premio). El dominio solo permite reemplazar la
/// política con el programa suspendido, así que si estaba activo se suspende, se reemplaza y se
/// reactiva dentro de la misma unidad de trabajo — nunca queda abierto a medio cambiar. Los premios ya
/// solicitados conservan su snapshot: el cambio solo aplica a los que se califiquen después.
/// </summary>
public static class UpdateTenantReferralProgramPolicyHandler
{
    private const string Operation = "Referrals.UpdateTenantReferralProgramPolicy.v1";

    public static async Task<Result<TenantReferralProgramResult>> Handle(
        UpdateTenantReferralProgramPolicyCommand command,
        IReferralProgramRepository programs,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<TenantReferralProgramResult>(actor.Error);

        if (command.ProgramId == Guid.Empty || command.Policy is null)
        {
            return Result.Failure<TenantReferralProgramResult>(
                new Error("ReferralProgram.InvalidProgram", "ProgramId and Policy are required.")
            );
        }

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.ProgramId,
            command.Policy.AttributionWindowDays,
            command.Policy.MinimumPaymentAmountCents,
            command.Policy.MinimumPaymentCurrency,
            command.Policy.WaitingPeriodDays,
            command.Policy.MaximumRewardsPerReferrerPerCalendarYear,
            command.Policy.RewardType,
            command.Policy.RewardDefinitionKey,
            command.Policy.RewardBasisPoints,
            command.Policy.RewardCapCents,
            command.Policy.RewardCurrency,
            command.ActorUserId
        );

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

                var resolved = ReferralProgramPolicyResolver.Resolve(command.Policy, program.Policy);
                if (resolved.IsFailure)
                    return Result.Failure<TenantReferralProgramResult>(resolved.Error);

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var wasActive = program.Status == ReferralProgramStatus.Active;
                if (wasActive)
                {
                    var suspended = program.Suspend(command.ActorUserId, nowUtc);
                    if (suspended.IsFailure)
                        return Result.Failure<TenantReferralProgramResult>(suspended.Error);
                }

                var replaced = program.ReplacePolicy(resolved.Value, command.ActorUserId, nowUtc);
                if (replaced.IsFailure)
                    return Result.Failure<TenantReferralProgramResult>(replaced.Error);

                if (wasActive)
                {
                    var reactivated = program.Activate(command.ActorUserId, nowUtc);
                    if (reactivated.IsFailure)
                        return Result.Failure<TenantReferralProgramResult>(reactivated.Error);
                }

                return Result.Success(TenantReferralProgramResult.From(program));
            },
            ct
        );
    }
}
