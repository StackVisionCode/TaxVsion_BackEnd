using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Growth.Tests.Domain;

/// <summary>
/// REF-1: premio del referidor en % del plan pagado (WalletCredit), código regenerable,
/// registro de envíos por canal y beneficio acreditado. El referido no recibe nada.
/// </summary>
public sealed class ReferralsWalletRewardDomainTests
{
    [Fact]
    public void Defaults_reward_the_referrer_with_a_wallet_percentage_of_the_plan_paid()
    {
        var policy = ReferralProgramPolicy.TenantToTenantDefaults();

        Assert.Equal(ReferralRewardType.WalletCredit, policy.RewardType);
        Assert.Equal(1_000, policy.RewardBasisPoints);
        Assert.Equal(5_000, policy.RewardCapCents);
        Assert.Equal("USD", policy.RewardCurrency);
    }

    [Theory]
    [InlineData(4_900, 490)] // Starter $49 -> $4.90
    [InlineData(12_900, 1_290)] // Pro $129 -> $12.90
    [InlineData(29_900, 2_990)] // Enterprise $299 -> $29.90
    [InlineData(299_000, 5_000)] // Enterprise anual $2,990 -> tope $50.00
    [InlineData(99, 9)] // 9.9 centavos -> redondeo hacia abajo
    [InlineData(0, 0)]
    public void Reward_is_percentage_of_price_paid_floored_and_capped(long pricePaidCents, long expectedCents)
    {
        var policy = ReferralProgramPolicy.TenantToTenantDefaults();

        Assert.Equal(expectedCents, policy.CalculateRewardCents(pricePaidCents));
    }

    [Fact]
    public void Non_monetary_reward_types_calculate_no_amount()
    {
        var policy = TrialExtensionPolicy();

        Assert.Equal(0, policy.CalculateRewardCents(12_900));
    }

    [Theory]
    [InlineData(0, 5_000L, "USD", "ReferralPolicy.InvalidRewardPercentage")]
    [InlineData(10_001, 5_000L, "USD", "ReferralPolicy.InvalidRewardPercentage")]
    [InlineData(1_000, 0L, "USD", "ReferralPolicy.InvalidRewardCap")]
    [InlineData(1_000, 5_000L, null, "ReferralPolicy.InvalidRewardCurrency")]
    [InlineData(1_000, 5_000L, "US", "ReferralPolicy.InvalidRewardCurrency")]
    public void Wallet_credit_policy_validates_percentage_cap_and_currency(
        int basisPoints,
        long cap,
        string? currency,
        string expectedError
    )
    {
        var result = ReferralProgramPolicy.CreateTenantToTenant(
            90,
            1,
            null,
            30,
            10,
            ReferralRewardType.WalletCredit,
            "referrals.test",
            rewardBasisPoints: basisPoints,
            rewardCapCents: cap,
            rewardCurrency: currency
        );

        Assert.True(result.IsFailure);
        Assert.Equal(expectedError, result.Error.Code);
    }

    [Fact]
    public void Wallet_credit_policy_allows_no_cap()
    {
        var result = ReferralProgramPolicy.CreateTenantToTenant(
            90,
            1,
            null,
            30,
            10,
            ReferralRewardType.WalletCredit,
            "referrals.test",
            rewardBasisPoints: 500,
            rewardCapCents: null,
            rewardCurrency: "usd"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.RewardCurrency);
        Assert.Equal(129_000 * 500 / 10_000, result.Value.CalculateRewardCents(129_000));
    }

    [Fact]
    public void Reward_amount_fields_are_rejected_on_a_non_monetary_reward()
    {
        var result = ReferralProgramPolicy.CreateTenantToTenant(
            90,
            1,
            null,
            30,
            10,
            ReferralRewardType.SubscriptionTrialExtension,
            "referrals.test",
            rewardBasisPoints: 1_000
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralPolicy.RewardAmountNotApplicable", result.Error.Code);
    }

    [Fact]
    public void Reward_case_freezes_the_calculation_snapshot()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = QualifiedAttribution(program, out var qualification);

        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "reward-snapshot",
                GrowthTestData.Sha('6'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;

        Assert.Equal(ReferralRewardType.WalletCredit, reward.RewardType);
        Assert.Equal(2_500, reward.PricePaidCents);
        Assert.Equal(1_000, reward.RewardBasisPoints);
        Assert.Equal(5_000, reward.RewardCapCents);
        Assert.Equal(250, reward.RewardAmountCents);
        Assert.Equal("USD", reward.RewardCurrency);
        Assert.Equal(GrowthTestData.ReferrerTenantId, reward.TenantId);
    }

    [Fact]
    public void Reward_case_rejects_a_payment_in_another_currency()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = GrowthTestData.CreateActiveAttribution(program, GrowthTestData.CreateReferralCode(program));
        var qualification = Evaluate(program, attribution, 2_500, "EUR");
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);

        var reward = ReferralRewardCase.Request(
            program,
            attribution,
            qualification,
            "reward-eur",
            GrowthTestData.Sha('7'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc.AddDays(1)
        );

        Assert.True(reward.IsFailure);
        Assert.Equal("ReferralReward.CurrencyMismatch", reward.Error.Code);
    }

    [Fact]
    public void Reward_case_is_not_created_when_the_percentage_rounds_to_zero()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = GrowthTestData.CreateActiveAttribution(program, GrowthTestData.CreateReferralCode(program));
        var qualification = Evaluate(program, attribution, 5, "USD"); // 5 centavos x 10 % = 0
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);

        var reward = ReferralRewardCase.Request(
            program,
            attribution,
            qualification,
            "reward-tiny",
            GrowthTestData.Sha('8'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc.AddDays(1)
        );

        Assert.True(reward.IsFailure);
        Assert.Equal("ReferralReward.NoRewardDue", reward.Error.Code);
    }

    [Fact]
    public void Superseding_a_code_revokes_it_and_a_revoked_code_registers_no_new_referrals()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);

        var superseded = code.Supersede(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddHours(1));
        var attribution = ReferralAttribution.Create(
            program,
            code,
            ReferralParticipantType.Tenant,
            GrowthTestData.RefereeTenantId,
            "after-revoke",
            GrowthTestData.Sha('9'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc.AddHours(2)
        );

        Assert.True(superseded.IsSuccess);
        Assert.Equal(ReferralCodeStatus.Revoked, code.Status);
        Assert.Equal(ReferralCode.SupersededReason, code.RevocationReason);
        Assert.True(code.EnsureUsable(GrowthTestData.NowUtc.AddHours(2)).IsFailure);
        Assert.True(attribution.IsFailure);
        Assert.Equal("ReferralCode.NotActive", attribution.Error.Code);
    }

    [Fact]
    public void An_attribution_made_before_the_code_was_replaced_keeps_qualifying()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var attribution = GrowthTestData.CreateActiveAttribution(program, code);

        Assert.True(code.Supersede(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddHours(1)).IsSuccess);
        var qualified = attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1));

        Assert.True(qualified.IsSuccess);
        Assert.Equal(ReferralAttributionStatus.Qualified, attribution.Status);
        Assert.Equal(code.Id, attribution.ReferralCodeId);
    }

    [Fact]
    public void Code_records_the_user_who_generated_it_and_the_attribution_copies_it_with_channel()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var ownerUserId = Guid.NewGuid();
        var code = ReferralCode
            .Create(
                program,
                ReferralParticipantType.Tenant,
                GrowthTestData.ReferrerTenantId,
                GrowthTestData.Sha('2'),
                "REF",
                "2026",
                GrowthTestData.NowUtc.AddMonths(3),
                "code-owner-user",
                GrowthTestData.Sha('3'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc,
                ownerUserId
            )
            .Value;
        var shareId = Guid.NewGuid();

        var attribution = ReferralAttribution
            .Create(
                program,
                code,
                ReferralParticipantType.Tenant,
                GrowthTestData.RefereeTenantId,
                "attribution-channel",
                GrowthTestData.Sha('4'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc,
                ReferralShareChannel.WhatsApp,
                shareId
            )
            .Value;

        Assert.Equal(ownerUserId, code.OwnerUserId);
        Assert.Equal(ownerUserId, attribution.ReferrerUserId);
        Assert.Equal(ReferralShareChannel.WhatsApp, attribution.Channel);
        Assert.Equal(shareId, attribution.ShareId);
    }

    [Fact]
    public void Attribution_rejects_an_undefined_channel()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);

        var attribution = ReferralAttribution.Create(
            program,
            code,
            ReferralParticipantType.Tenant,
            GrowthTestData.RefereeTenantId,
            "bad-channel",
            GrowthTestData.Sha('4'),
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc,
            (ReferralShareChannel)99
        );

        Assert.True(attribution.IsFailure);
        Assert.Equal("ReferralAttribution.InvalidChannel", attribution.Error.Code);
    }

    [Fact]
    public void Any_user_can_record_a_share_with_its_channel_under_the_referrer_tenant()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var employee = Guid.NewGuid();

        var share = ReferralCodeShare.Record(
            code,
            ReferralShareChannel.Email,
            employee,
            "  ana@cliente.com ",
            GrowthTestData.NowUtc.AddHours(1)
        );

        Assert.True(share.IsSuccess);
        Assert.Equal(code.Id, share.Value.ReferralCodeId);
        Assert.Equal(ReferralShareChannel.Email, share.Value.Channel);
        Assert.Equal(employee, share.Value.SharedByUserId);
        Assert.Equal("ana@cliente.com", share.Value.RecipientHint);
        Assert.Equal(GrowthTestData.ReferrerTenantId, share.Value.TenantId);
    }

    [Fact]
    public void A_revoked_code_cannot_be_shared_and_invalid_shares_are_rejected()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var code = GrowthTestData.CreateReferralCode(program);
        var activeCode = GrowthTestData.CreateReferralCode(program);

        var badChannel = ReferralCodeShare.Record(
            activeCode,
            (ReferralShareChannel)99,
            Guid.NewGuid(),
            null,
            GrowthTestData.NowUtc
        );
        var longHint = ReferralCodeShare.Record(
            activeCode,
            ReferralShareChannel.Sms,
            Guid.NewGuid(),
            new string('x', 201),
            GrowthTestData.NowUtc
        );
        var noActor = ReferralCodeShare.Record(activeCode, ReferralShareChannel.Sms, Guid.Empty, null, GrowthTestData.NowUtc);
        Assert.True(code.Supersede(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddHours(1)).IsSuccess);
        var revoked = ReferralCodeShare.Record(
            code,
            ReferralShareChannel.Copy,
            Guid.NewGuid(),
            null,
            GrowthTestData.NowUtc.AddHours(2)
        );

        Assert.Equal("ReferralShare.InvalidChannel", badChannel.Error.Code);
        Assert.Equal("ReferralShare.InvalidRecipientHint", longHint.Error.Code);
        Assert.Equal("Referrals.InvalidActor", noActor.Error.Code);
        Assert.Equal("ReferralCode.NotActive", revoked.Error.Code);
    }

    [Fact]
    public void Referrer_benefit_records_the_credited_amount_and_is_never_deleted_on_reversal()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = QualifiedAttribution(program, out var qualification);
        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "reward-benefit",
                GrowthTestData.Sha('a'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;
        var systemActor = Guid.NewGuid();

        var benefit = ReferrerBenefit.Record(reward, systemActor, GrowthTestData.NowUtc.AddDays(31));

        Assert.True(benefit.IsSuccess);
        Assert.Equal(reward.Id, benefit.Value.RewardCaseId);
        Assert.Equal(attribution.Id, benefit.Value.AttributionId);
        Assert.Equal(1_000, benefit.Value.AppliedBasisPoints);
        Assert.Equal(250, benefit.Value.TotalBenefitWalletCents);
        Assert.Equal("USD", benefit.Value.Currency);
        Assert.Equal(GrowthTestData.ReferrerTenantId, benefit.Value.TenantId);
        Assert.Null(benefit.Value.ReversedAtUtc);

        var reversedAt = GrowthTestData.NowUtc.AddDays(40);
        Assert.True(benefit.Value.MarkReversed(reversedAt).IsSuccess);
        Assert.True(benefit.Value.MarkReversed(reversedAt.AddDays(1)).IsSuccess);
        Assert.Equal(reversedAt, benefit.Value.ReversedAtUtc);
    }

    [Fact]
    public void Referrer_benefit_requires_a_monetary_reward_case()
    {
        var policy = TrialExtensionPolicy();
        var program = ReferralProgram
            .Create(
                "T2T-NONMONETARY",
                "Non monetary",
                ReferralProgramScope.Platform,
                tenantScopeId: null,
                ReferralFlowType.TenantToTenant,
                policy,
                GrowthTestData.NowUtc.AddDays(-1),
                GrowthTestData.NowUtc.AddYears(1),
                "program-nonmonetary",
                GrowthTestData.Sha('1'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;
        Assert.True(program.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        var attribution = QualifiedAttribution(program, out var qualification);
        var reward = ReferralRewardCase
            .Request(
                program,
                attribution,
                qualification,
                "reward-nonmonetary",
                GrowthTestData.Sha('b'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;

        var benefit = ReferrerBenefit.Record(reward, Guid.NewGuid(), GrowthTestData.NowUtc.AddDays(31));

        Assert.True(benefit.IsFailure);
        Assert.Equal("ReferrerBenefit.NotMonetary", benefit.Error.Code);
        Assert.Equal(0, reward.RewardAmountCents);
    }

    private static ReferralProgramPolicy TrialExtensionPolicy() =>
        ReferralProgramPolicy
            .CreateTenantToTenant(
                90,
                1,
                null,
                30,
                10,
                ReferralRewardType.SubscriptionTrialExtension,
                "referrals.trial"
            )
            .Value;

    private static ReferralAttribution QualifiedAttribution(
        ReferralProgram program,
        out ReferralQualification qualification
    )
    {
        var attribution = GrowthTestData.CreateActiveAttribution(program, GrowthTestData.CreateReferralCode(program));
        qualification = GrowthTestData.CreateQualifiedReferral(program, attribution);
        Assert.True(attribution.MarkQualified(GrowthTestData.ActorId, GrowthTestData.NowUtc.AddDays(1)).IsSuccess);
        return attribution;
    }

    private static ReferralQualification Evaluate(
        ReferralProgram program,
        ReferralAttribution attribution,
        long amountCents,
        string currency
    ) =>
        ReferralQualification
            .Evaluate(
                program,
                attribution,
                Guid.NewGuid(),
                Guid.NewGuid(),
                QualifyingPaymentSource.PaymentApp,
                amountCents,
                currency,
                isFirstSuccessfulPayment: true,
                annualRewardSlotAvailable: true,
                GrowthTestData.NowUtc.AddDays(1),
                $"qualification-{Guid.NewGuid():N}",
                GrowthTestData.Sha('c'),
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;
}
