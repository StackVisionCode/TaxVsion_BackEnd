using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Cancel/Expire sobre PlanChangeRequest (upgrade AwaitingPayment).</summary>
public sealed class PlanChangeRequestCancelExpireTests
{
    private static PlanChangeRequest Awaiting() =>
        PlanChangeRequest
            .Create(
                tenantSubscriptionId: Guid.NewGuid(),
                tenantId: Guid.NewGuid(),
                fromPlanId: Guid.NewGuid(),
                fromPlanVersionId: Guid.NewGuid(),
                fromPlanCode: "starter",
                toPlanId: Guid.NewGuid(),
                toPlanVersionId: Guid.NewGuid(),
                toPlanCode: "pro",
                toBillingCycle: BillingCycle.Monthly,
                requestedByUserId: Guid.NewGuid(),
                nowUtc: DateTime.UtcNow,
                chargeAmountCents: 4900,
                chargeCurrency: "USD",
                paymentIdempotencyKey: Guid.NewGuid().ToString("N")
            )
            .Value;

    [Fact]
    public void Cancel_moves_awaiting_payment_to_cancelled()
    {
        var request = Awaiting();
        var actor = Guid.NewGuid();

        var result = request.Cancel(actor, DateTime.UtcNow, "user closed the window");

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanChangeRequestStatus.Cancelled, request.Status);
        Assert.Equal(actor, request.CancelledByUserId);
    }

    [Fact]
    public void Cancel_rejects_an_applied_request()
    {
        var request = Awaiting();
        request.MarkPaymentSucceeded(Guid.NewGuid(), DateTime.UtcNow);

        var result = request.Cancel(Guid.NewGuid(), DateTime.UtcNow, null);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanChangeRequest.AlreadyPaid", result.Error.Code);
    }

    [Fact]
    public void Cancel_is_idempotent_from_cancelled_or_payment_failed()
    {
        var cancelled = Awaiting();
        cancelled.Cancel(Guid.NewGuid(), DateTime.UtcNow, null);
        Assert.True(cancelled.Cancel(Guid.NewGuid(), DateTime.UtcNow, null).IsSuccess);

        var failed = Awaiting();
        failed.MarkPaymentFailed(Guid.NewGuid(), DateTime.UtcNow);
        Assert.True(failed.Cancel(Guid.NewGuid(), DateTime.UtcNow, null).IsSuccess);
    }

    [Fact]
    public void Expire_closes_an_elapsed_awaiting_payment()
    {
        var request = Awaiting();
        request.AttachCheckout(Guid.NewGuid(), "https://x", DateTime.UtcNow.AddMinutes(-1));

        request.Expire(DateTime.UtcNow);

        Assert.Equal(PlanChangeRequestStatus.Expired, request.Status);
    }

    [Fact]
    public void Expire_leaves_an_open_session_untouched()
    {
        var request = Awaiting();
        request.AttachCheckout(Guid.NewGuid(), "https://x", DateTime.UtcNow.AddHours(1));

        request.Expire(DateTime.UtcNow);

        Assert.Equal(PlanChangeRequestStatus.AwaitingPayment, request.Status);
    }
}
