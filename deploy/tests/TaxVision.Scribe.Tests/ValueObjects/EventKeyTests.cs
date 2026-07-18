using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.ValueObjects;

public sealed class EventKeyTests
{
    [Theory]
    [InlineData("auth.password_reset_requested.v1")]
    [InlineData("Sig.Invitation.V1")]
    public void Create_normalizes_and_accepts_valid_keys(string candidate)
    {
        var result = EventKey.Create(candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(candidate.Trim().ToLowerInvariant(), result.Value.Value);
    }

    [Fact]
    public void Create_rejects_empty_key()
    {
        var result = EventKey.Create("");

        Assert.True(result.IsFailure);
        Assert.Equal("EventKey.Empty", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_malformed_key()
    {
        var result = EventKey.Create("not a valid key!");

        Assert.True(result.IsFailure);
        Assert.Equal("EventKey.Format", result.Error.Code);
    }
}
