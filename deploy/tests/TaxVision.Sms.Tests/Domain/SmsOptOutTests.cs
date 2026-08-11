using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Tests.Domain;

public sealed class SmsOptOutTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    private static SmsOptOut NewSubscribed() =>
        SmsOptOut.CreateSubscribed(Guid.NewGuid(), Guid.NewGuid(), PhoneE164.Create("+18095551234").Value, Now);

    [Fact]
    public void CreateSubscribed_starts_subscribed_and_not_opted_out()
    {
        var optOut = NewSubscribed();

        Assert.Equal(SmsOptOutStatus.Subscribed, optOut.Status);
        Assert.False(optOut.IsOptedOut);
        Assert.Equal("+18095551234", optOut.PhoneE164);
    }

    [Fact]
    public void OptOut_sets_opted_out_and_records_keyword()
    {
        var optOut = NewSubscribed();

        optOut.OptOut("STOP", Now);

        Assert.True(optOut.IsOptedOut);
        Assert.Equal(SmsOptOutStatus.OptedOut, optOut.Status);
        Assert.Equal("STOP", optOut.LastKeyword);
        Assert.Equal(Now, optOut.OptedOutAtUtc);
    }

    [Fact]
    public void OptIn_resubscribes()
    {
        var optOut = NewSubscribed();
        optOut.OptOut("STOP", Now);

        optOut.OptIn("START", Now.AddMinutes(5));

        Assert.False(optOut.IsOptedOut);
        Assert.Equal(SmsOptOutStatus.Subscribed, optOut.Status);
        Assert.Equal("START", optOut.LastKeyword);
        Assert.Equal(Now.AddMinutes(5), optOut.OptedInAtUtc);
    }
}
