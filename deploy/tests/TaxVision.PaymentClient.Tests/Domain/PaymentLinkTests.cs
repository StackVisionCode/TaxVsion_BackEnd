using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class PaymentLinkTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;

    [Fact]
    public void Create_with_a_zero_amount_fails()
    {
        var result = PaymentLink.Create(
            Guid.NewGuid(), null, Money.Zero("USD"), Purpose(), PaymentLinkToken.Generate(), TimeSpan.FromDays(1), Guid.Empty, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.InvalidAmount", result.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void Create_with_an_invalid_expiration_fails(int days)
    {
        var result = PaymentLink.Create(
            Guid.NewGuid(), null, Money.Create(1999, "USD").Value, Purpose(), PaymentLinkToken.Generate(), TimeSpan.FromDays(days), Guid.Empty, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.InvalidExpiration", result.Error.Code);
    }

    [Fact]
    public void Create_starts_Active_and_stamps_the_expiration_from_now()
    {
        var link = CreateActiveLink(TimeSpan.FromDays(7));

        Assert.Equal(PaymentLinkStatus.Active, link.Status);
        Assert.Equal(NowUtc.AddDays(7), link.ExpiresAtUtc);
        Assert.Null(link.RelatedTenantPaymentId);
        Assert.Null(link.UsedAtUtc);
    }

    [Fact]
    public void IsRedeemable_is_false_once_past_ExpiresAtUtc_even_if_still_Active()
    {
        var link = CreateActiveLink(TimeSpan.FromMinutes(30));

        Assert.True(link.IsRedeemable(NowUtc.AddMinutes(1)));
        Assert.False(link.IsRedeemable(NowUtc.AddMinutes(31)));
    }

    [Fact]
    public void MarkAsUsed_without_an_attached_payment_attempt_is_rejected()
    {
        var link = CreateActiveLink();

        var result = link.MarkAsUsed(NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.NoPaymentAttempt", result.Error.Code);
    }

    [Fact]
    public void AttachPaymentAttempt_then_MarkAsUsed_reaches_Used()
    {
        var link = CreateActiveLink();
        var paymentId = Guid.NewGuid();

        var attach = link.AttachPaymentAttempt(paymentId, NowUtc);
        var used = link.MarkAsUsed(NowUtc.AddSeconds(1));

        Assert.True(attach.IsSuccess);
        Assert.True(used.IsSuccess);
        Assert.Equal(PaymentLinkStatus.Used, link.Status);
        Assert.Equal(paymentId, link.RelatedTenantPaymentId);
        Assert.Equal(NowUtc.AddSeconds(1), link.UsedAtUtc);
    }

    [Fact]
    public void MarkAsUsed_a_second_time_is_rejected()
    {
        var link = CreateActiveLink();
        link.AttachPaymentAttempt(Guid.NewGuid(), NowUtc);
        link.MarkAsUsed(NowUtc);

        var result = link.MarkAsUsed(NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void AttachPaymentAttempt_on_an_expired_link_is_rejected()
    {
        var link = CreateActiveLink(TimeSpan.FromMinutes(1));

        var result = link.AttachPaymentAttempt(Guid.NewGuid(), NowUtc.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.NotRedeemable", result.Error.Code);
    }

    [Fact]
    public void Revoke_with_no_reason_fails()
    {
        var link = CreateActiveLink();

        var result = link.Revoke("  ", NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.InvalidReason", result.Error.Code);
    }

    [Fact]
    public void Revoke_turns_an_Active_link_off()
    {
        var link = CreateActiveLink();

        var result = link.Revoke("duplicate link", NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentLinkStatus.Revoked, link.Status);
        Assert.False(link.IsRedeemable(NowUtc));
    }

    [Fact]
    public void Revoke_a_Used_link_is_rejected()
    {
        var link = CreateActiveLink();
        link.AttachPaymentAttempt(Guid.NewGuid(), NowUtc);
        link.MarkAsUsed(NowUtc);

        var result = link.Revoke("too late", NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Expire_turns_an_Active_link_off()
    {
        var link = CreateActiveLink(TimeSpan.FromMinutes(1));

        var result = link.Expire(NowUtc.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentLinkStatus.Expired, link.Status);
    }

    [Fact]
    public void Expire_a_Revoked_link_is_rejected()
    {
        var link = CreateActiveLink();
        link.Revoke("duplicate link", NowUtc);

        var result = link.Expire(NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLink.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkBlockedAfterExcessiveFailures_increments_the_counter_without_revoking_before_the_limit()
    {
        var link = CreateActiveLink();

        for (var i = 0; i < PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink - 1; i++)
            link.MarkBlockedAfterExcessiveFailures(NowUtc);

        Assert.Equal(PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink - 1, link.FailedRedemptionAttempts);
        Assert.Equal(PaymentLinkStatus.Active, link.Status);
    }

    [Fact]
    public void MarkBlockedAfterExcessiveFailures_revokes_the_link_once_the_limit_is_reached()
    {
        var link = CreateActiveLink();

        for (var i = 0; i < PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink; i++)
            link.MarkBlockedAfterExcessiveFailures(NowUtc);

        Assert.Equal(PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink, link.FailedRedemptionAttempts);
        Assert.Equal(PaymentLinkStatus.Revoked, link.Status);
        Assert.False(link.IsRedeemable(NowUtc));
    }

    [Fact]
    public void MarkBlockedAfterExcessiveFailures_is_a_noop_once_the_link_is_no_longer_Active()
    {
        var link = CreateActiveLink();
        link.Revoke("duplicate link", NowUtc);

        link.MarkBlockedAfterExcessiveFailures(NowUtc);

        Assert.Equal(0, link.FailedRedemptionAttempts);
        Assert.Equal(PaymentLinkStatus.Revoked, link.Status);
    }

    private static PaymentPurpose Purpose() => PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-001").Value;

    private static PaymentLink CreateActiveLink(TimeSpan? expiration = null) =>
        PaymentLink.Create(
            Guid.NewGuid(), null, Money.Create(1999, "USD").Value, Purpose(), PaymentLinkToken.Generate(),
            expiration ?? TimeSpan.FromDays(7), Guid.Empty, NowUtc).Value;
}
