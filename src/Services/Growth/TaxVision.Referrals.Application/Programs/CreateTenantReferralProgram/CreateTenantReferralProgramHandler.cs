using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Application.Programs.Common;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;

public static class CreateTenantReferralProgramHandler
{
    private const string Operation = "Referrals.CreateTenantReferralProgram.v1";

    public static async Task<Result<TenantReferralProgramResult>> Handle(
        CreateTenantReferralProgramCommand command,
        IReferralProgramRepository programs,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<TenantReferralProgramResult>(actor.Error);

        if (!IsUtc(command.StartsAtUtc) || (command.EndsAtUtc is { } end && !IsUtc(end)))
        {
            return Failure("ReferralProgram.InvalidUtcPeriod", "StartsAtUtc and EndsAtUtc must use DateTimeKind.Utc.");
        }

        var policy = ResolvePolicy(command.Policy);
        if (policy.IsFailure)
            return Result.Failure<TenantReferralProgramResult>(policy.Error);

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.ProgramCode?.Trim().ToUpperInvariant(),
            command.Name?.Trim(),
            command.StartsAtUtc,
            command.EndsAtUtc,
            policy.Value.AttributionWindowDays,
            policy.Value.PaymentSource,
            policy.Value.QualifyingEvent,
            policy.Value.MinimumPaymentAmountCents,
            policy.Value.MinimumPaymentCurrency,
            policy.Value.WaitingPeriodDays,
            policy.Value.MaximumRewardsPerReferrerPerCalendarYear,
            policy.Value.RewardType,
            policy.Value.RewardDefinitionKey,
            policy.Value.RefereeBenefitType,
            policy.Value.RefereeBenefitPercentageBasisPoints,
            policy.Value.RefereeBenefitFixedAmountCents,
            policy.Value.RefereeBenefitCurrency,
            policy.Value.RefereeBenefitExpirationDays,
            command.ActorUserId
        );

        return await idempotency.ExecuteAsync(
            PlatformTenant.Id,
            Operation,
            PlatformTenant.Id,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var created = ReferralProgram.Create(
                    command.ProgramCode ?? string.Empty,
                    command.Name ?? string.Empty,
                    ReferralProgramScope.Platform,
                    tenantScopeId: null,
                    ReferralFlowType.TenantToTenant,
                    policy.Value,
                    command.StartsAtUtc,
                    command.EndsAtUtc,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    timeProvider.GetUtcNow().UtcDateTime
                );
                if (created.IsFailure)
                    return Result.Failure<TenantReferralProgramResult>(created.Error);

                await programs.AddAsync(created.Value, operationCt);
                return Result.Success(TenantReferralProgramResult.From(created.Value));
            },
            ct
        );
    }

    private static Result<ReferralProgramPolicy> ResolvePolicy(TenantReferralProgramPolicyInput? input)
    {
        var defaults = ReferralProgramPolicy.TenantToTenantDefaults();
        return ReferralProgramPolicy.CreateTenantToTenant(
            input?.AttributionWindowDays ?? defaults.AttributionWindowDays,
            input?.MinimumPaymentAmountCents ?? defaults.MinimumPaymentAmountCents,
            input?.MinimumPaymentCurrency ?? defaults.MinimumPaymentCurrency,
            input?.WaitingPeriodDays ?? defaults.WaitingPeriodDays,
            input?.MaximumRewardsPerReferrerPerCalendarYear ?? defaults.MaximumRewardsPerReferrerPerCalendarYear,
            input?.RewardType ?? defaults.RewardType,
            input?.RewardDefinitionKey ?? defaults.RewardDefinitionKey,
            input?.RefereeBenefitType ?? defaults.RefereeBenefitType,
            input?.RefereeBenefitPercentageBasisPoints ?? defaults.RefereeBenefitPercentageBasisPoints,
            input?.RefereeBenefitFixedAmountCents ?? defaults.RefereeBenefitFixedAmountCents,
            input?.RefereeBenefitCurrency ?? defaults.RefereeBenefitCurrency,
            input?.RefereeBenefitExpirationDays ?? defaults.RefereeBenefitExpirationDays
        );
    }

    private static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;

    private static Result<TenantReferralProgramResult> Failure(string code, string message) =>
        Result.Failure<TenantReferralProgramResult>(new Error(code, message));
}
