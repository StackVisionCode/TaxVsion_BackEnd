using System.Text.Json;
using Microsoft.Extensions.Options;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Tests.Security;

public sealed class ReferralCodeTokenGeneratorTests
{
    [Fact]
    public void Generator_is_deterministic_domain_bound_and_redacted()
    {
        var generator = new HmacSha256ReferralCodeTokenGenerator(
            Options.Create(
                new ReferralCodeTokenHashingOptions { Pepper = "unit-test-referral-root-secret-32-bytes-minimum" }
            )
        );
        var programId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var ownerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var first = generator.Generate(programId, ownerId, "issue-one");
        var replay = generator.Generate(programId, ownerId, " issue-one ");
        var otherOwner = generator.Generate(programId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "issue-one");
        var otherKey = generator.Generate(programId, ownerId, "issue-two");

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.Reveal(), replay.Value.Reveal());
        Assert.NotEqual(first.Value.Reveal(), otherOwner.Value.Reveal());
        Assert.NotEqual(first.Value.Reveal(), otherKey.Value.Reveal());
        Assert.StartsWith("TVR-", first.Value.Reveal(), StringComparison.Ordinal);
        Assert.Equal(36, first.Value.Reveal().Length);
        Assert.Equal("<redacted>", first.Value.ToString());
        Assert.DoesNotContain(first.Value.Reveal(), JsonSerializer.Serialize(first.Value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Generator_rejects_invalid_idempotency_keys(string key)
    {
        IReferralCodeTokenGenerator generator = new HmacSha256ReferralCodeTokenGenerator(
            Options.Create(
                new ReferralCodeTokenHashingOptions { Pepper = "unit-test-referral-root-secret-32-bytes-minimum" }
            )
        );

        var result = generator.Generate(Guid.NewGuid(), Guid.NewGuid(), key);

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralCode.Token.InvalidIdempotencyKey", result.Error.Code);
    }
}
