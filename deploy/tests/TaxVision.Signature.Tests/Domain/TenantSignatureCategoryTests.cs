using TaxVision.Signature.Domain.Categories;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

public sealed class TenantSignatureCategoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void Create_trims_and_normalizes_the_name()
    {
        var result = TenantSignatureCategory.Create(Tenant, User, "  Payroll   Forms  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Payroll   Forms".Trim(), result.Value.Name);
        Assert.Equal("PAYROLL FORMS", result.Value.NormalizedName);
        Assert.False(result.Value.IsArchived);
        Assert.Equal(Tenant, result.Value.TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    public void Create_rejects_invalid_names(string name)
    {
        var result = TenantSignatureCategory.Create(Tenant, User, name);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Category.Name", result.Error.Code);
    }

    [Theory]
    [InlineData("Fiscal")]
    [InlineData("other")]
    [InlineData("  bankauth ")]
    public void Create_rejects_reserved_system_names(string name)
    {
        var result = TenantSignatureCategory.Create(Tenant, User, name);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Category.Reserved", result.Error.Code);
    }

    [Fact]
    public void Rename_updates_name_and_normalized()
    {
        var category = TenantSignatureCategory.Create(Tenant, User, "Old").Value;

        var result = category.Rename("New Name");

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", category.Name);
        Assert.Equal("NEW NAME", category.NormalizedName);
    }

    [Fact]
    public void Archive_and_unarchive_toggle_the_flag()
    {
        var category = TenantSignatureCategory.Create(Tenant, User, "Payroll").Value;

        category.Archive();
        Assert.True(category.IsArchived);

        category.Unarchive();
        Assert.False(category.IsArchived);
    }
}
