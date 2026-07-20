using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Tests.Domain;

public sealed class ReferralsDomainTests
{
    [Fact]
    public void Tenant_to_tenant_defaults_match_the_approved_non_monetary_policy()
    {
        var policy = ReferralProgramPolicy.TenantToTenantDefaults();

        Assert.Equal(QualifyingPaymentSource.PaymentApp, policy.PaymentSource);
        Assert.Equal(QualifyingEventRule.FirstSuccessfulPayment, policy.QualifyingEvent);
        Assert.Equal(30, policy.WaitingPeriodDays);
        Assert.Equal(10, policy.MaximumRewardsPerReferrerPerCalendarYear);
        Assert.Equal(ReferralRewardType.SubscriptionFeatureGrant, policy.RewardType);
    }

    [Fact]
    public void Taxpayer_to_taxpayer_program_can_be_modeled_but_not_activated()
    {
        var policy = ReferralProgramPolicy
            .CreateTaxpayerToTaxpayerDraft(
                attributionWindowDays: 30,
                minimumPaymentAmountCents: 1_000,
                minimumPaymentCurrency: "USD",
                waitingPeriodDays: 14,
                maximumRewardsPerReferrerPerCalendarYear: 5,
                ReferralRewardType.SubscriptionFeatureGrant,
                "tenant.taxpayer-referral"
            )
            .Value;
        var program = ReferralProgram
            .Create(
                "TAXPAYER-DRAFT",
                "Taxpayer draft",
                ReferralProgramScope.Tenant,
                GrowthTestData.RefereeTenantId,
                ReferralFlowType.TaxpayerToTaxpayer,
                policy,
                GrowthTestData.NowUtc,
                GrowthTestData.NowUtc.AddMonths(6),
                "taxpayer-program-1",
                GrowthTestData.Sha('e'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;

        var activation = program.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc);

        Assert.True(activation.IsFailure);
        Assert.Equal("ReferralProgram.TaxpayerFlowDeferred", activation.Error.Code);
        Assert.Equal(ReferralProgramStatus.Draft, program.Status);
    }

    [Fact]
    public void Tenant_to_tenant_artifacts_are_owned_by_the_participant_they_expose()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);
        var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
        Assert.True(
            attribution.MarkQualified(
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            ).IsSuccess
        );
        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "reward-1",
                GrowthTestData.Sha('f'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;

        Assert.Equal(PlatformTenant.Id, program.TenantId);
        Assert.Equal(GrowthTestData.ReferrerTenantId, code.TenantId);
        Assert.Equal(GrowthTestData.RefereeTenantId, attribution.TenantId);
        Assert.Equal(GrowthTestData.RefereeTenantId, qualification.TenantId);
        Assert.Equal(GrowthTestData.ReferrerTenantId, reward.TenantId);
    }

    [Fact]
    public void Self_referral_is_rejected()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);

        var attribution = ReferralAttribution.Create(
            program,
            code,
            ReferralParticipantType.Tenant,
            GrowthTestData.ReferrerTenantId,
            "self-referral",
            GrowthTestData.Sha('0'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );

        Assert.True(attribution.IsFailure);
        Assert.Equal("ReferralAttribution.SelfReferral", attribution.Error.Code);
    }

    [Fact]
    public void Only_first_successful_payment_qualifies_and_waiting_period_is_frozen()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);
        var paymentAt = GrowthTestData.NowUtc.AddDays(2);

        var rejected = ReferralQualification.Evaluate(
            program,
            attribution,
            Guid.NewGuid(),
            Guid.NewGuid(),
            QualifyingPaymentSource.PaymentApp,
            10_000,
            "USD",
            isFirstSuccessfulPayment: false,
            annualRewardSlotAvailable: true,
            paymentAt,
            "not-first",
            GrowthTestData.Sha('1'),
            GrowthTestData.ActorId,
            paymentAt
        );
        var qualified = ReferralQualification.Evaluate(
            program,
            attribution,
            Guid.NewGuid(),
            Guid.NewGuid(),
            QualifyingPaymentSource.PaymentApp,
            10_000,
            "USD",
            isFirstSuccessfulPayment: true,
            annualRewardSlotAvailable: true,
            paymentAt,
            "first",
            GrowthTestData.Sha('2'),
            GrowthTestData.ActorId,
            paymentAt
        );

        Assert.Equal(ReferralQualificationDecision.Rejected, rejected.Value.Decision);
        Assert.Equal("NotFirstSuccessfulPayment", rejected.Value.RejectionReasonCode);
        Assert.Equal(ReferralQualificationDecision.Qualified, qualified.Value.Decision);
        Assert.Equal(paymentAt.AddDays(30), qualified.Value.RewardEligibleAtUtc);
    }

    [Fact]
    public void Granted_reward_moves_monotonically_through_clawback_to_reversed()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);
        var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
        var qualifiedAt = GrowthTestData.NowUtc.AddDays(1);
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, qualifiedAt).IsSuccess);
        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "reward-lifecycle",
                GrowthTestData.Sha('3'),
                GrowthTestData.ActorId,
                qualifiedAt
            )
            .Value;
        var eligibleAt = qualification.RewardEligibleAtUtc!.Value;

        Assert.True(reward.BeginGrant(GrowthTestData.ActorId, eligibleAt).IsSuccess);
        Assert.True(
            reward.ConfirmGranted(
                "subscription-grant-123",
                GrowthTestData.ActorId,
                eligibleAt.AddMinutes(1)
            ).IsSuccess
        );
        Assert.True(
            reward.RequestClawback(
                "payment chargeback lost",
                GrowthTestData.ActorId,
                eligibleAt.AddDays(1)
            ).IsSuccess
        );
        Assert.True(
            reward.ConfirmReversed(
                GrowthTestData.ActorId,
                eligibleAt.AddDays(1).AddMinutes(1)
            ).IsSuccess
        );

        Assert.Equal(ReferralRewardCaseStatus.Reversed, reward.Status);
        Assert.True(
            reward.ConfirmReversed(
                GrowthTestData.ActorId,
                eligibleAt.AddDays(2)
            ).IsSuccess
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Referral_fingerprints_must_be_canonical_sha256_hex(string fingerprint)
    {
        var result = ReferralProgram.Create(
            "INVALID-FINGERPRINT",
            "Invalid fingerprint",
            ReferralProgramScope.Platform,
            tenantScopeId: null,
            ReferralFlowType.TenantToTenant,
            ReferralProgramPolicy.TenantToTenantDefaults(),
            GrowthTestData.NowUtc,
            GrowthTestData.NowUtc.AddDays(1),
            "invalid-fingerprint",
            fingerprint,
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralProgram.InvalidPayloadFingerprint", result.Error.Code);
    }

    [Fact]
    public void Referral_policy_rejects_undefined_reward_type()
    {
        var result = ReferralProgramPolicy.CreateTenantToTenant(
            attributionWindowDays: 90,
            minimumPaymentAmountCents: 1,
            minimumPaymentCurrency: null,
            waitingPeriodDays: 30,
            maximumRewardsPerReferrerPerCalendarYear: 10,
            rewardType: (ReferralRewardType)999,
            rewardDefinitionKey: "referrals.invalid"
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralPolicy.InvalidRewardType", result.Error.Code);
    }

    [Fact]
    public void Referral_program_rejects_undefined_scope_and_flow()
    {
        var policy = ReferralProgramPolicy.TenantToTenantDefaults();
        var invalidScope = ReferralProgram.Create(
            "INVALID-SCOPE",
            "Invalid scope",
            (ReferralProgramScope)999,
            tenantScopeId: null,
            ReferralFlowType.TenantToTenant,
            policy,
            GrowthTestData.NowUtc,
            GrowthTestData.NowUtc.AddDays(1),
            "invalid-scope",
            GrowthTestData.Sha('a'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );
        var invalidFlow = ReferralProgram.Create(
            "INVALID-FLOW",
            "Invalid flow",
            ReferralProgramScope.Platform,
            tenantScopeId: null,
            (ReferralFlowType)999,
            policy,
            GrowthTestData.NowUtc,
            GrowthTestData.NowUtc.AddDays(1),
            "invalid-flow",
            GrowthTestData.Sha('b'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );

        Assert.True(invalidScope.IsFailure);
        Assert.Equal("ReferralProgram.InvalidScope", invalidScope.Error.Code);
        Assert.True(invalidFlow.IsFailure);
        Assert.Equal("ReferralProgram.InvalidFlow", invalidFlow.Error.Code);
    }

    [Fact]
    public void Reward_attempt_rejects_undefined_operation()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);
        var qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
        Assert.True(
            attribution.MarkQualified(
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            ).IsSuccess
        );
        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "invalid-operation-reward",
                GrowthTestData.Sha('c'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;

        var result = ReferralRewardAttempt.Start(
            reward,
            (ReferralRewardOperation)999,
            "invalid-operation",
            GrowthTestData.Sha('d'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc.AddDays(1)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralRewardAttempt.InvalidOperation", result.Error.Code);
    }
}
