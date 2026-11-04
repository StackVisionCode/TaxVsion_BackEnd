using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class DoingBusinessAsNameTests
{
    [Theory]
    [InlineData("IRS Tax Pros")]
    [InlineData("Tax Pros (irs approved)")]
    [InlineData("Internal Revenue Service Helpers")]
    public void Rejects_irs_as_a_word(string name)
    {
        Assert.Equal(DoingBusinessAsName.MentionsIrs, DoingBusinessAsName.CreateOptional(name).Error);
    }

    [Theory]
    [InlineData("First Tax Services")]
    [InlineData("Birsk Tax")]
    public void Accepts_names_that_only_contain_the_letters(string name)
    {
        Assert.Equal(name, DoingBusinessAsName.CreateOptional(name).Value!.Value);
    }
}
