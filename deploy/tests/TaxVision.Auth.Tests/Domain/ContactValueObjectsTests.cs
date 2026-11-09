using TaxVision.Auth.Domain.Common;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Teléfono y host: las mismas reglas que estaban sueltas en el agregado, en un solo lugar.</summary>
public sealed class ContactValueObjectsTests
{
    [Theory]
    [InlineData(" +1 305-555-0100 ", "+1 305-555-0100")]
    [InlineData("3055550", "3055550")]
    public void PhoneNumber_Valid_KeepsTheTrimmedText(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Create(raw).Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("305-555-CALL")]
    [InlineData("123456789012345678901")]
    public void PhoneNumber_Invalid_ReturnsUserPhone(string raw)
    {
        Assert.Equal("User.Phone", PhoneNumber.Create(raw).Error.Code);
    }

    [Fact]
    public void HostName_NormalizesCaseAndTrims()
    {
        Assert.Equal("portal.oficina.com", HostName.Create(" Portal.Oficina.COM ").Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    public void HostName_Invalid_ReturnsHostInvalid(string? raw)
    {
        Assert.Equal("TenantDomain.HostInvalid", HostName.Create(raw).Error.Code);
    }
}
