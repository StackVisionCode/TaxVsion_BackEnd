using TaxVision.Sms.Application.Messages;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;

namespace TaxVision.Sms.Tests.Application;

public sealed class SmsFailoverPolicyTests
{
    [Fact]
    public void Outcome_unknown_never_fails_over() =>
        Assert.False(SmsFailoverPolicy.ShouldTryNext(Rejected(SmsSendFailure.OutcomeUnknown)));

    [Fact]
    public void Carrier_opt_out_never_fails_over() =>
        Assert.False(SmsFailoverPolicy.ShouldTryNext(Rejected(SmsSendFailure.RecipientOptedOut)));

    [Fact]
    public void Accepted_never_fails_over() =>
        Assert.False(SmsFailoverPolicy.ShouldTryNext(new SmsSendResult(true, "prov-1", null, null)));

    [Fact]
    public void Provider_unavailable_fails_over() =>
        Assert.True(SmsFailoverPolicy.ShouldTryNext(Rejected("provider.down")));

    [Fact]
    public void Media_unsupported_fails_over() =>
        Assert.True(SmsFailoverPolicy.ShouldTryNext(Rejected(SmsErrors.MediaNotSupported.Code)));

    [Fact]
    public void Rejection_without_a_code_fails_over() => Assert.True(SmsFailoverPolicy.ShouldTryNext(Rejected(null)));

    private static SmsSendResult Rejected(string? code) => new(false, null, code, "No.");
}
