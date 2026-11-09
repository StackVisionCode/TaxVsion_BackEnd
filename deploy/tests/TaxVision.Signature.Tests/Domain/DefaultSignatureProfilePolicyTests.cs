using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>SIG-7: la regla de una sola default por ámbito vive en el dominio.</summary>
public sealed class DefaultSignatureProfilePolicyTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public void SetDefault_UnsetsPrevious()
    {
        var previous = NewProfile();
        previous.MarkDefault();
        var target = NewProfile();

        var result = DefaultSignatureProfilePolicy.SetDefault([previous, target], target);

        Assert.True(result.IsSuccess);
        Assert.False(previous.IsDefault);
        Assert.True(target.IsDefault);
    }

    // El rechazo no deja al ámbito sin default.
    [Fact]
    public void SetDefault_ArchivedTarget_KeepsPrevious()
    {
        var previous = NewProfile();
        previous.MarkDefault();
        var target = NewProfile();
        target.Archive();

        var result = DefaultSignatureProfilePolicy.SetDefault([previous, target], target);

        Assert.True(result.IsFailure);
        Assert.True(previous.IsDefault);
        Assert.False(target.IsDefault);
    }

    private SignatureProfile NewProfile() =>
        SignatureProfile.Create(_tenantId, Guid.NewGuid(), null, "Signature", Guid.NewGuid(), 10, 10).Value;
}
