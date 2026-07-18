using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class UnmatchedIncomingEmailTests
{
    private static EmailAddress Address(string value) => EmailAddress.Create(value).Value;

    [Fact]
    public void Create_fails_when_tenantId_is_empty()
    {
        var result = UnmatchedIncomingEmail.Create(
            Guid.Empty,
            Address("someone@example.com"),
            "Subject",
            "provider-msg-1",
            DateTime.UtcNow,
            UnmatchedReason.NoCustomerMatch
        );

        Assert.True(result.IsFailure);
        Assert.Equal("UnmatchedIncomingEmail.TenantIdRequired", result.Error.Code);
    }

    [Fact]
    public void Create_with_NoCustomerMatch_sets_a_24_hour_ttl()
    {
        var receivedAt = DateTime.UtcNow;

        var result = UnmatchedIncomingEmail.Create(
            Guid.NewGuid(),
            Address("someone@example.com"),
            "Subject",
            "provider-msg-1",
            receivedAt,
            UnmatchedReason.NoCustomerMatch
        );

        Assert.True(result.IsSuccess);
        var entity = result.Value;
        Assert.Equal(UnmatchedReason.NoCustomerMatch, entity.Reason);
        Assert.Equal(entity.CreatedAtUtc.AddHours(24), entity.ExpiresAtUtc);
    }

    [Fact]
    public void Create_with_AuthenticationFailed_sets_a_longer_ttl_than_NoCustomerMatch()
    {
        var result = UnmatchedIncomingEmail.Create(
            Guid.NewGuid(),
            Address("someone@example.com"),
            "Subject",
            "provider-msg-1",
            DateTime.UtcNow,
            UnmatchedReason.AuthenticationFailed
        );

        Assert.True(result.IsSuccess);
        var entity = result.Value;
        Assert.Equal(UnmatchedReason.AuthenticationFailed, entity.Reason);
        Assert.Equal(entity.CreatedAtUtc.AddDays(90), entity.ExpiresAtUtc);
        Assert.True(entity.ExpiresAtUtc > entity.CreatedAtUtc.AddHours(24));
    }

    [Fact]
    public void Create_normalizes_the_from_address()
    {
        var result = UnmatchedIncomingEmail.Create(
            Guid.NewGuid(),
            Address("Someone@Example.com"),
            "Subject",
            "provider-msg-1",
            DateTime.UtcNow,
            UnmatchedReason.NoCustomerMatch
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("someone@example.com", result.Value.FromAddress);
    }
}
