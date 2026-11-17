using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>La renovación del plan con sus asientos y add-ons es un solo cobro con una línea por cosa.</summary>
public sealed class BundledRenewalChargeTests
{
    private static readonly Guid SeatId = Guid.NewGuid();

    [Fact]
    public void The_bundle_payment_keeps_each_line_with_its_target()
    {
        var payment = SubscriptionRenewalCharge
            .BuildPayment(Due(), PaymentProviderCode.Stripe, TestPlatform.Settings)
            .Value;

        Assert.Equal(4_400, payment.Amount.AmountCents);
        Assert.Equal(2, payment.Lines.Count);
        Assert.Contains(payment.Lines, line => line.TargetKind == "Seat" && line.TargetId == SeatId);
    }

    [Fact]
    public void The_fallback_charge_also_carries_the_lines()
    {
        var command = SubscriptionRenewalCharge.BuildLegacyCommand(Due(), PaymentProviderCode.Stripe);

        Assert.Equal(2, command.Lines!.Count);
    }

    private static SubscriptionRenewalDueIntegrationEvent Due() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            TenantSubscriptionId = Guid.NewGuid(),
            PlanCode = "pro",
            PeriodStartUtc = DateTime.UtcNow,
            PeriodEndUtc = DateTime.UtcNow.AddMonths(1),
            IdempotencyKey = $"subscription-renewal-{Guid.NewGuid():N}",
            AmountCents = 4_400,
            Currency = "USD",
            Lines =
            [
                new SaaSChargeLine("Pro plan", 1, 2_900, "Plan", Guid.NewGuid()),
                new SaaSChargeLine("Standard seat", 1, 1_500, "Seat", SeatId),
            ],
        };
}
