using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Tests.Domain;

public class ProviderWatchSubscriptionTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static ProviderWatchSubscription CreateValidSubscription() =>
        ProviderWatchSubscription
            .Create(AccountId, ProviderCode.Gmail, "history-1", "projects/tv/topics/gmail-push", Now.AddDays(7), Now)
            .Value;

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = ProviderWatchSubscription.Create(
            AccountId,
            ProviderCode.Gmail,
            "history-1",
            "projects/tv/topics/gmail-push",
            Now.AddDays(7),
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.Value.AccountId);
        Assert.Equal(ProviderCode.Gmail, result.Value.ProviderCode);
        Assert.Equal("history-1", result.Value.SubscriptionRef);
        Assert.Equal(ProviderWatchStatus.Active, result.Value.Status);
        Assert.Equal(0, result.Value.FailureCount);
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        var result = ProviderWatchSubscription.Create(
            Guid.Empty,
            ProviderCode.Gmail,
            "history-1",
            null,
            Now.AddDays(7),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderWatchSubscription.AccountId", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankSubscriptionRef_Fails(string subscriptionRef)
    {
        var result = ProviderWatchSubscription.Create(
            AccountId,
            ProviderCode.Gmail,
            subscriptionRef,
            null,
            Now.AddDays(7),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderWatchSubscription.SubscriptionRef", result.Error.Code);
    }

    [Fact]
    public void Create_WithSubscriptionRefTooLong_Fails()
    {
        var subscriptionRef = new string('r', 501);

        var result = ProviderWatchSubscription.Create(
            AccountId,
            ProviderCode.Gmail,
            subscriptionRef,
            null,
            Now.AddDays(7),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderWatchSubscription.SubscriptionRef", result.Error.Code);
    }

    [Fact]
    public void Create_WithExpiresAtInThePast_Fails()
    {
        var result = ProviderWatchSubscription.Create(
            AccountId,
            ProviderCode.Gmail,
            "history-1",
            null,
            Now.AddMinutes(-1),
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderWatchSubscription.ExpiresAtUtc", result.Error.Code);
    }

    [Fact]
    public void Renew_ReplacesReferenceAndResetsFailureCount()
    {
        var subscription = CreateValidSubscription();
        subscription.RecordRenewalFailure();
        subscription.RecordRenewalFailure();

        var renewedAt = Now.AddDays(6);
        subscription.Renew("history-2", renewedAt.AddDays(7), renewedAt);

        Assert.Equal("history-2", subscription.SubscriptionRef);
        Assert.Equal(renewedAt.AddDays(7), subscription.ExpiresAtUtc);
        Assert.Equal(renewedAt, subscription.LastRenewedAtUtc);
        Assert.Equal(ProviderWatchStatus.Active, subscription.Status);
        Assert.Equal(0, subscription.FailureCount);
    }

    [Fact]
    public void RecordRenewalFailure_IncrementsFailureCount()
    {
        var subscription = CreateValidSubscription();

        subscription.RecordRenewalFailure();
        subscription.RecordRenewalFailure();

        Assert.Equal(2, subscription.FailureCount);
    }

    [Fact]
    public void MarkFailed_SetsFailedStatus()
    {
        var subscription = CreateValidSubscription();
        subscription.RecordRenewalFailure();
        subscription.RecordRenewalFailure();
        subscription.RecordRenewalFailure();

        subscription.MarkFailed();

        Assert.Equal(ProviderWatchStatus.Failed, subscription.Status);
        Assert.Equal(3, subscription.FailureCount);
    }
}
