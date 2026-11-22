using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Application.Rewards;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.GetReferralRewardNotice;
using TaxVision.Referrals.Application.Rewards.GrantWalletCreditReward;
using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// El referidor se entera de su premio: calificado, acreditado, cancelado o revertido. Growth publica cada evento una
/// sola vez y con un id estable por premio, así una reentrega no duplica el aviso.
/// </summary>
public sealed class ReferralRewardEventsTests
{
    private static readonly Guid Referrer = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly ReferralRewardNotice Notice = new(
        Referrer,
        Guid.Parse("50000000-0000-0000-0000-000000000001"),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        990,
        "USD",
        new DateTime(2026, 12, 17, 0, 0, 0, DateTimeKind.Utc)
    );

    // El comando de calificación devuelve este aviso en cascada (ver ReferralsIdempotencyApplicationTests).
    [Fact]
    public void A_qualified_referral_notice_carries_the_reward()
    {
        var paymentId = Guid.NewGuid();

        var qualified = Assert.IsType<ReferralQualifiedIntegrationEvent>(
            new ReferralNoticeEvents().Qualified(Notice, paymentId, "corr-qualify")
        );

        Assert.Equal(paymentId, qualified.QualifyingPaymentId);
        Assert.Equal(Referrer, qualified.TenantId);
        Assert.Equal(990, qualified.RewardAmountCents);
        Assert.Equal("USD", qualified.RewardCurrency);
        Assert.Equal(Notice.EligibleAtUtc, qualified.RewardAvailableAtUtc);
    }

    [Fact]
    public async Task A_credited_reward_publishes_granted_once()
    {
        var granted = Bus(ConfirmWalletCreditRewardOutcome.Granted);
        var redelivered = Bus(ConfirmWalletCreditRewardOutcome.AlreadyGranted);

        await WalletCreditAppliedConsumer.Handle(Applied(reverse: false), granted, Logger, default);
        await WalletCreditAppliedConsumer.Handle(Applied(reverse: false), redelivered, Logger, default);

        var published = Assert.IsType<ReferralRewardGrantedIntegrationEvent>(Assert.Single(granted.Published));
        Assert.Equal(Referrer, published.TenantId);
        Assert.Equal(Notice.GrantId, published.GrantId);
        Assert.Equal(990, published.RewardAmountCents);
        Assert.Empty(redelivered.Published);
    }

    [Fact]
    public async Task A_reversed_reward_publishes_reversed_once()
    {
        var reversed = Bus(reversal: ConfirmWalletReversalOutcome.Reversed);
        var redelivered = Bus(reversal: ConfirmWalletReversalOutcome.AlreadyReversed);

        await WalletCreditAppliedConsumer.Handle(Applied(reverse: true), reversed, Logger, default);
        await WalletCreditAppliedConsumer.Handle(Applied(reverse: true), redelivered, Logger, default);

        var published = Assert.IsType<ReferralRewardReversedIntegrationEvent>(Assert.Single(reversed.Published));
        Assert.Equal(Notice.RewardCaseId, published.RewardCaseId);
        Assert.Equal(990, published.RewardAmountCents);
        Assert.Empty(redelivered.Published);
    }

    [Fact]
    public async Task A_reward_cancelled_by_a_refund_publishes_rejected()
    {
        var bus = ClawbackBus(ClawbackWalletCreditRewardOutcome.Cancelled);

        await PaymentRefundedConsumer.Handle(
            Refunded(),
            bus,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        var rejected = Assert.IsType<ReferralRewardRejectedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(Referrer, rejected.TenantId);
        Assert.Equal("ReferredPaymentRefunded", rejected.ReasonCode);
    }

    [Fact]
    public async Task A_reward_cancelled_by_a_lost_dispute_publishes_rejected()
    {
        var bus = ClawbackBus(ClawbackWalletCreditRewardOutcome.Cancelled);

        await PaymentChargebackChangedConsumer.Handle(
            LostDispute(),
            bus,
            NullLogger<PaymentChargebackChangedIntegrationEvent>.Instance,
            default
        );

        var rejected = Assert.IsType<ReferralRewardRejectedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("ReferredPaymentDisputeLost", rejected.ReasonCode);
    }

    [Fact]
    public async Task A_reversal_request_waits_for_the_wallet_before_telling_anyone()
    {
        var bus = ClawbackBus(ClawbackWalletCreditRewardOutcome.ReversalRequested);

        await PaymentRefundedConsumer.Handle(
            Refunded(),
            bus,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        Assert.DoesNotContain(bus.Published, message => message is ReferralRewardRejectedIntegrationEvent);
    }

    [Fact]
    public void The_same_reward_always_yields_the_same_event_id()
    {
        var first = ReferralRewardEvents.Granted(Notice, Guid.NewGuid(), "corr");
        var again = ReferralRewardEvents.Granted(Notice, Guid.NewGuid(), "corr");
        var other = ReferralRewardEvents.Rejected(Notice, "ReviewRejected", "corr");

        Assert.Equal(first.EventId, again.EventId);
        Assert.NotEqual(first.EventId, other.EventId);
    }

    [Fact]
    public void Event_ids_are_valid_uuids_for_the_other_services()
    {
        // Communication valida el id como UUID RFC 4122 (versión 1-8 y variante 8-b): si no, descarta el aviso.
        var ids = new[]
        {
            ReferralRewardEvents.Qualified(Notice, Guid.NewGuid(), "corr").EventId,
            ReferralRewardEvents.Granted(Notice, Guid.NewGuid(), "corr").EventId,
            ReferralRewardEvents.Rejected(Notice, "ReviewRejected", "corr").EventId,
            ReferralRewardEvents.Reversed(Notice, Guid.NewGuid(), "corr").EventId,
        };

        foreach (var id in ids)
            Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", id.ToString());
    }

    private static readonly NullLogger<WalletCreditAppliedIntegrationEventMarker> Logger = new();

    private static ScriptedBus Bus(
        ConfirmWalletCreditRewardOutcome credit = ConfirmWalletCreditRewardOutcome.Granted,
        ConfirmWalletReversalOutcome reversal = ConfirmWalletReversalOutcome.Reversed
    ) =>
        new(message =>
            message switch
            {
                ConfirmWalletCreditRewardCommand => Result.Success(credit),
                ConfirmWalletReversalCommand => Result.Success(reversal),
                GetReferralRewardNoticeQuery => Result.Success<ReferralRewardNotice?>(Notice),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );

    private static ScriptedBus ClawbackBus(ClawbackWalletCreditRewardOutcome outcome) =>
        new(message =>
            message switch
            {
                ClawbackWalletCreditRewardCommand => Result.Success(
                    new ClawbackWalletCreditRewardResult(outcome, null, Notice)
                ),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );

    private static WalletCreditAppliedIntegrationEvent Applied(bool reverse) =>
        new()
        {
            TenantId = Referrer,
            ReferenceType = WalletCreditRequest.ReferenceType,
            ReferenceId = Notice.RewardCaseId,
            Reverse = reverse,
            Succeeded = true,
            LedgerEntryId = Guid.NewGuid(),
        };

    private static OnboardingPaymentSucceededIntegrationEvent Paid() =>
        new()
        {
            OnboardingId = Guid.NewGuid(),
            SaaSPaymentId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            AmountPaidCents = 9_900,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            ProviderPaymentReference = "pi_test",
        };

    private static PaymentRefundedIntegrationEvent Refunded() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 9_900,
            DiscountAmountCents = 0,
            NetAmountCents = 9_900,
            Currency = "USD",
            RefundReference = "ref",
            RefundDeltaAmountCents = 9_900,
            CumulativeRefundedAmountCents = 9_900,
            RefundedAtUtc = DateTime.UtcNow,
        };

    private static PaymentChargebackChangedIntegrationEvent LostDispute() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 9_900,
            DiscountAmountCents = 0,
            NetAmountCents = 9_900,
            Currency = "USD",
            DisputeId = "chargeback:1",
            DisputeStatus = "lost",
            DisputeVersion = 2,
            DisputedAmountCents = 9_900,
            ChangedAtUtc = DateTime.UtcNow,
        };
}
