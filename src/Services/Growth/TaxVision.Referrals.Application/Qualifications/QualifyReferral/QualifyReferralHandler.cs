using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Qualifications.QualifyReferral;

public static class QualifyReferralHandler
{
    private const string Operation = "QualifyReferral";

    public static async Task<Result<QualifyReferralResult>> Handle(
        QualifyReferralCommand command,
        IReferralAttributionRepository attributions,
        IReferralProgramRepository programs,
        IReferralQualificationRepository qualifications,
        IReferralRewardCaseRepository rewards,
        IReferralRewardQuota rewardQuota,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<QualifyReferralResult>(actor.Error);

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.AttributionId,
            command.QualifyingEventId,
            command.PaymentId,
            command.PaymentSource,
            command.PaymentAmountCents,
            command.PaymentCurrency.Trim().ToUpperInvariant(),
            command.IsFirstSuccessfulPayment,
            command.PaymentSucceededAtUtc
        );

        // La key de negocio se deriva del EventId: el mismo evento no puede producir dos
        // efectos aunque un redelivery cambie accidentalmente la key del producer.
        var businessKey = $"event:{command.QualifyingEventId:N}";

        return await idempotency.ExecuteAsync(
            command.TenantId,
            Operation,
            command.AttributionId,
            businessKey,
            fingerprint,
            async operationCt =>
            {
                var attribution = await attributions.GetByIdAsync(command.AttributionId, command.TenantId, operationCt);
                if (attribution is null)
                    return NotFound();

                var program = await programs.GetForEvaluationAsync(attribution.ProgramId, operationCt);
                if (program is null)
                    return NotFound();

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var evaluated = ReferralQualification.Evaluate(
                    program,
                    attribution,
                    command.QualifyingEventId,
                    command.PaymentId,
                    command.PaymentSource,
                    command.PaymentAmountCents,
                    command.PaymentCurrency,
                    command.IsFirstSuccessfulPayment,
                    annualRewardSlotAvailable: true,
                    command.PaymentSucceededAtUtc,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc
                );
                if (evaluated.IsFailure)
                    return Result.Failure<QualifyReferralResult>(evaluated.Error);

                var qualification = evaluated.Value;
                if (qualification.Decision == ReferralQualificationDecision.Qualified)
                {
                    // En T2T el owner de la cuota es el tenant del referrer (mismo GUID que
                        // ReferrerId — ver SqlReferralRewardQuota XML doc). El comando corre bajo
                        // el tenant del referee (quien paga), así que se pasa explícito.
                    var slotReserved = await rewardQuota.TryReserveAnnualSlotAsync(
                        attribution.ReferrerId,
                        program.Id,
                        attribution.ReferrerId,
                        command.PaymentSucceededAtUtc.Year,
                        program.Policy.MaximumRewardsPerReferrerPerCalendarYear,
                        qualification.Id,
                        operationCt
                    );

                    if (!slotReserved)
                    {
                        var rejected = ReferralQualification.Evaluate(
                            program,
                            attribution,
                            command.QualifyingEventId,
                            command.PaymentId,
                            command.PaymentSource,
                            command.PaymentAmountCents,
                            command.PaymentCurrency,
                            command.IsFirstSuccessfulPayment,
                            annualRewardSlotAvailable: false,
                            command.PaymentSucceededAtUtc,
                            command.IdempotencyKey,
                            fingerprint,
                            command.ActorUserId,
                            nowUtc
                        );
                        if (rejected.IsFailure)
                            return Result.Failure<QualifyReferralResult>(rejected.Error);

                        qualification = rejected.Value;
                    }
                }

                await qualifications.AddAsync(qualification, operationCt);

                ReferralRewardCase? reward = null;
                if (qualification.Decision == ReferralQualificationDecision.Qualified)
                {
                    var qualified = attribution.MarkQualified(command.ActorUserId, nowUtc);
                    if (qualified.IsFailure)
                        return Result.Failure<QualifyReferralResult>(qualified.Error);

                    var rewardKey = $"qualification:{qualification.Id:N}";
                    var rewardFingerprint = CanonicalPayloadFingerprint.Compute(
                        qualification.Id,
                        attribution.ReferrerType,
                        attribution.ReferrerId,
                        program.Policy.RewardType,
                        program.Policy.RewardDefinitionKey
                    );
                    var requested = ReferralRewardCase.Request(
                        program,
                        attribution,
                        qualification,
                        rewardKey,
                        rewardFingerprint,
                        command.ActorUserId,
                        nowUtc
                    );
                    if (requested.IsFailure)
                        return Result.Failure<QualifyReferralResult>(requested.Error);

                    reward = requested.Value;
                    await rewards.AddAsync(reward, operationCt);
                }

                return Result.Success(
                    new QualifyReferralResult(
                        qualification.Id,
                        qualification.Decision,
                        qualification.RejectionReasonCode,
                        reward?.Id,
                        WasReplay: false
                    )
                );
            },
            ct
        );
    }

    private static Result<QualifyReferralResult> NotFound() =>
        Result.Failure<QualifyReferralResult>(
            new Error("ReferralAttribution.NotFound", "The referral attribution does not exist.")
        );
}
