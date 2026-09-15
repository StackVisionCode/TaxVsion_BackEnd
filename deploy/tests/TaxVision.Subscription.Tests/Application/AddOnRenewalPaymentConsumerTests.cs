using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.AddOns.IntegrationEvents;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Cierra el loop de cobro del add-on: el intent (AddOnRenewalDue) → PaymentApp cobra →
/// PaymentSucceeded/Failed → estos consumers aplican CompleteRenewal / FailRenewal por IdempotencyKey.</summary>
public sealed class AddOnRenewalPaymentConsumerTests
{
    [Fact]
    public async Task PaymentSucceeded_advances_the_addon_period_for_the_matching_renewal()
    {
        var (addOn, key, newEnd) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();

        await AddOnRenewalPaymentSucceededConsumer.Handle(
            new AddOnRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = addOn.TenantId,
                TenantAddOnId = addOn.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = key,
                ExternalPaymentReference = "ext-ref",
                PaidAtUtc = DateTime.UtcNow,
            },
            new FakeTenantAddOnRepo([addOn]),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AddOnStatus.Active, addOn.Status);
        Assert.Equal(newEnd, addOn.CurrentPeriodEndUtc);
        Assert.Equal(RenewalStatus.Succeeded, addOn.Renewals.First().Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task PaymentFailed_without_retry_moves_the_addon_to_past_due()
    {
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();

        await AddOnRenewalPaymentFailedConsumer.Handle(
            new AddOnRenewalPaymentFailedIntegrationEvent
            {
                TenantId = addOn.TenantId,
                TenantAddOnId = addOn.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = key,
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = false,
                NextRetryAtUtc = null,
            },
            new FakeTenantAddOnRepo([addOn]),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AddOnStatus.PastDue, addOn.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    private static (TenantAddOn AddOn, string Key, DateTime NewEnd) ActiveAddOnWithScheduledRenewal()
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("email.addon").Value,
                "Email",
                "Email",
                "module",
                false,
                [BillingCycle.Monthly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        var addOn = TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                definition,
                1,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                nowUtc
            )
            .Value;

        var newEnd = addOn.CurrentPeriodEndUtc.AddMonths(1);
        const string key = "addon-renewal-key-1";
        addOn.BeginRenewal(key, newEnd, Guid.Empty, nowUtc);
        return (addOn, key, newEnd);
    }
}
