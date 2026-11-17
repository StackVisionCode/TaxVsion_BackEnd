using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Codes.Application.Compensations.CompensateRedemption;
using TaxVision.Codes.Application.Compensations.FindRedemptionByReservation;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.DisputeHold;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Reembolso o contracargo de un pago con códigos de descuento: cada canje se compensa. Reembolso total → el código
/// vuelve a estar disponible; parcial o contracargo → queda gastado (solo se anota). Sin promociones, como antes.
/// </summary>
public sealed class PaymentPromotionCompensationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Reservation = Guid.NewGuid();
    private static readonly Guid Redemption = Guid.NewGuid();

    [Fact]
    public async Task Full_refund_restores_the_code_availability()
    {
        var bus = Bus();
        var refund = Refunded(cumulativeCents: 9_900);

        await PaymentRefundedConsumer.Handle(
            refund,
            bus,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        var compensate = Assert.Single(bus.Invoked.OfType<CompensateRedemptionCommand>());
        Assert.Equal(Tenant, compensate.TenantId);
        Assert.Equal(Redemption, compensate.RedemptionId);
        Assert.Equal(CodeCompensationType.RestoreAvailability, compensate.Type);
        Assert.Equal(2_000, compensate.AdjustmentAmountCents);
        Assert.Equal(refund.EventId, compensate.SourceEventId);
    }

    [Fact]
    public async Task Partial_refund_keeps_it_consumed()
    {
        var bus = Bus();

        await PaymentRefundedConsumer.Handle(
            Refunded(cumulativeCents: 4_000),
            bus,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        var compensate = Assert.Single(bus.Invoked.OfType<CompensateRedemptionCommand>());
        Assert.Equal(CodeCompensationType.KeepConsumed, compensate.Type);
        Assert.Equal(0, compensate.AdjustmentAmountCents);
    }

    [Fact]
    public async Task Replay_compensates_once()
    {
        var refund = Refunded(cumulativeCents: 9_900);
        var first = Bus();
        var second = Bus();

        await PaymentRefundedConsumer.Handle(
            refund,
            first,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );
        await PaymentRefundedConsumer.Handle(
            refund,
            second,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        // La misma clave y el mismo evento: el caso de uso responde la primera compensación, no crea otra.
        Assert.Equal(
            Assert.Single(first.Invoked.OfType<CompensateRedemptionCommand>()).IdempotencyKey,
            Assert.Single(second.Invoked.OfType<CompensateRedemptionCommand>()).IdempotencyKey
        );
    }

    [Fact]
    public async Task An_open_dispute_holds_the_reward_and_leaves_the_code_alone()
    {
        // La disputa todavía puede ganarse: el premio se retiene y nada se revierte.
        var bus = Bus();

        await PaymentChargebackChangedConsumer.Handle(
            Chargeback("opened"),
            bus,
            NullLogger<PaymentChargebackChangedIntegrationEvent>.Instance,
            default
        );

        var hold = Assert.IsType<HoldReferralRewardForDisputeCommand>(Assert.Single(bus.Invoked));
        Assert.Equal("chargeback:1", hold.DisputeId);
    }

    [Fact]
    public async Task A_lost_dispute_claws_back_the_reward_and_keeps_the_code_consumed()
    {
        var bus = Bus();

        await PaymentChargebackChangedConsumer.Handle(
            Chargeback("lost"),
            bus,
            NullLogger<PaymentChargebackChangedIntegrationEvent>.Instance,
            default
        );

        Assert.Single(bus.Invoked.OfType<ClawbackWalletCreditRewardCommand>());
        Assert.Equal(
            CodeCompensationType.KeepConsumed,
            Assert.Single(bus.Invoked.OfType<CompensateRedemptionCommand>()).Type
        );
    }

    [Fact]
    public async Task A_won_dispute_releases_the_held_reward()
    {
        var bus = Bus();

        await PaymentChargebackChangedConsumer.Handle(
            Chargeback("won"),
            bus,
            NullLogger<PaymentChargebackChangedIntegrationEvent>.Instance,
            default
        );

        var release = Assert.IsType<ReleaseReferralRewardDisputeHoldCommand>(Assert.Single(bus.Invoked));
        Assert.Equal("chargeback:1", release.DisputeId);
    }

    [Fact]
    public async Task An_already_compensated_code_does_not_fail_the_event()
    {
        var bus = Bus(
            compensation: Result.Failure<CompensateRedemptionResponse>(
                new Error("Codes.CodeReservation.InvalidTransition", "Already compensated.")
            )
        );

        var ex = await Record.ExceptionAsync(() =>
            PaymentRefundedConsumer.Handle(
                Refunded(cumulativeCents: 9_900),
                bus,
                NullLogger<PaymentRefundedIntegrationEvent>.Instance,
                default
            )
        );

        Assert.Null(ex);
    }

    [Fact]
    public async Task A_code_that_was_never_committed_is_skipped()
    {
        var bus = Bus(noRedemption: true);

        await PaymentRefundedConsumer.Handle(
            Refunded(cumulativeCents: 9_900),
            bus,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        Assert.Empty(bus.Invoked.OfType<CompensateRedemptionCommand>());
    }

    [Fact]
    public async Task An_old_event_without_promotions_only_runs_the_referral_clawback()
    {
        var bus = Bus();

        await PaymentRefundedConsumer.Handle(
            Refunded(cumulativeCents: 9_900) with
            {
                Promotions = null,
            },
            bus,
            NullLogger<PaymentRefundedIntegrationEvent>.Instance,
            default
        );

        Assert.IsType<ClawbackWalletCreditRewardCommand>(Assert.Single(bus.Invoked));
    }

    private static ScriptedBus Bus(
        RedemptionToCompensate? redemption = default,
        Result<CompensateRedemptionResponse>? compensation = null,
        bool noRedemption = false
    )
    {
        var found = redemption ?? new RedemptionToCompensate(Redemption, 2_000, "USD");
        return new ScriptedBus(message =>
            message switch
            {
                ClawbackWalletCreditRewardCommand => Result.Success(
                    new ClawbackWalletCreditRewardResult(ClawbackWalletCreditRewardOutcome.NoReward, null)
                ),
                HoldReferralRewardForDisputeCommand or ReleaseReferralRewardDisputeHoldCommand => Result.Success(
                    ReferralRewardDisputeHoldOutcome.NoReward
                ),
                FindRedemptionByReservationQuery => Result.Success<RedemptionToCompensate?>(
                    noRedemption ? null : found
                ),
                CompensateRedemptionCommand => compensation
                    ?? Result.Success(
                        new CompensateRedemptionResponse(
                            Guid.NewGuid(),
                            Redemption,
                            Reservation,
                            "RestoreAvailability",
                            2_000,
                            2_000,
                            "USD",
                            true,
                            DateTime.UtcNow
                        )
                    ),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );
    }

    private static PaymentRefundedIntegrationEvent Refunded(long cumulativeCents) =>
        new()
        {
            TenantId = Tenant,
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 11_900,
            DiscountAmountCents = 2_000,
            NetAmountCents = 9_900,
            Currency = "USD",
            RefundReference = $"ref:{cumulativeCents}",
            RefundDeltaAmountCents = cumulativeCents,
            CumulativeRefundedAmountCents = cumulativeCents,
            RefundedAtUtc = DateTime.UtcNow,
            Promotions = [new PaymentPromotionData(Reservation, 2_000, 1)],
        };

    private static PaymentChargebackChangedIntegrationEvent Chargeback(string status) =>
        new()
        {
            TenantId = Tenant,
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 11_900,
            DiscountAmountCents = 2_000,
            NetAmountCents = 9_900,
            Currency = "USD",
            DisputeId = "chargeback:1",
            DisputeStatus = status,
            DisputeVersion = 1,
            DisputedAmountCents = 9_900,
            ChangedAtUtc = DateTime.UtcNow,
            Promotions = [new PaymentPromotionData(Reservation, 2_000, 1)],
        };
}
