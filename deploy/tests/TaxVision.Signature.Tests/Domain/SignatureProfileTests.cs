using TaxVision.Signature.Domain.Profiles;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

public sealed class SignatureProfileTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static SignatureProfile NewProfile(Guid? owner = null) =>
        SignatureProfile.Create(Tenant, User, owner ?? User, "Blue ink", Guid.NewGuid(), 100, 50).Value;

    [Fact]
    public void Create_sets_fields_and_is_not_default_by_itself()
    {
        var fileId = Guid.NewGuid();

        var result = SignatureProfile.Create(Tenant, User, User, "Blue ink", fileId, 120, 60);

        Assert.True(result.IsSuccess);
        var profile = result.Value;
        Assert.Equal(Tenant, profile.TenantId);
        Assert.Equal(User, profile.OwnerUserId);
        Assert.False(profile.IsOffice);
        Assert.Equal(fileId, profile.FileId);
        Assert.False(profile.IsDefault);
        Assert.False(profile.IsArchived);
    }

    [Fact]
    public void Create_allows_office_scope_without_owner()
    {
        var result = SignatureProfile.Create(Tenant, User, ownerUserId: null, "Office", Guid.NewGuid(), 100, 50);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.OwnerUserId);
        Assert.True(result.Value.IsOffice);
    }

    [Fact]
    public void Create_rejects_blank_label()
    {
        var result = SignatureProfile.Create(Tenant, User, User, "   ", Guid.NewGuid(), 100, 50);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.Label", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_file()
    {
        var result = SignatureProfile.Create(Tenant, User, User, "Blue ink", Guid.Empty, 100, 50);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.File", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_nonpositive_dimensions()
    {
        var result = SignatureProfile.Create(Tenant, User, User, "Blue ink", Guid.NewGuid(), 0, 50);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.Dimensions", result.Error.Code);
    }

    [Fact]
    public void Rename_changes_the_label()
    {
        var profile = NewProfile();

        var result = profile.Rename("  Black ink  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Black ink", profile.Label);
    }

    [Fact]
    public void MarkDefault_fails_when_archived()
    {
        var profile = NewProfile();
        profile.Archive();

        var result = profile.MarkDefault();

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Profile.ArchivedDefault", result.Error.Code);
    }

    [Fact]
    public void Archive_clears_the_default_flag()
    {
        var profile = NewProfile();
        profile.MarkDefault();

        profile.Archive();

        Assert.True(profile.IsArchived);
        Assert.False(profile.IsDefault);
    }
}
