using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public class PartialCopyAudienceTests
{
    [Fact]
    public void All_includes_any_signer()
    {
        var all = PartialCopyAudience.All();

        Assert.True(all.Includes(Guid.NewGuid()));
        Assert.Equal(PartialCopyAudienceKind.All, all.Kind);
    }

    [Fact]
    public void Specific_rejects_empty_set()
    {
        var result = PartialCopyAudience.Specific(Array.Empty<Guid>());

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.PartialCopyAudience.Empty", result.Error.Code);
    }

    [Fact]
    public void Specific_rejects_null()
    {
        var result = PartialCopyAudience.Specific(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.PartialCopyAudience.Null", result.Error.Code);
    }

    [Fact]
    public void Specific_rejects_guid_empty()
    {
        var result = PartialCopyAudience.Specific([Guid.NewGuid(), Guid.Empty]);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.PartialCopyAudience.InvalidId", result.Error.Code);
    }

    [Fact]
    public void Specific_includes_only_listed_signers()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();
        var audience = PartialCopyAudience.Specific([alice, bob]).Value;

        Assert.True(audience.Includes(alice));
        Assert.True(audience.Includes(bob));
        Assert.False(audience.Includes(carol));
    }

    [Fact]
    public void Specific_dedupes_duplicates()
    {
        var alice = Guid.NewGuid();
        var audience = PartialCopyAudience.Specific([alice, alice]).Value;

        Assert.Single(audience.SpecificSignerIds);
    }
}
