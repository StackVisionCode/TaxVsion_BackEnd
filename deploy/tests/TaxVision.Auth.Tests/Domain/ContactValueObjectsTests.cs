using TaxVision.Auth.Domain.Common;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Teléfono y host: las mismas reglas que estaban sueltas en el agregado, en un solo lugar.</summary>
public sealed class ContactValueObjectsTests
{
    // Se guarda en E.164, igual que Customer; sin código, 10 dígitos son de +1.
    [Theory]
    [InlineData(" +1 305-555-0100 ", "+13055550100")]
    [InlineData("(305) 555-0100", "+13055550100")]
    [InlineData("305.555.0100", "+13055550100")]
    [InlineData("13055550100", "+13055550100")]
    [InlineData("1 (809) 555-0100", "+18095550100")]
    [InlineData("+52 55 1234 5678", "+525512345678")]
    public void PhoneNumber_Valid_IsStoredAsE164(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Create(raw).Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("3055550")]
    [InlineData("305-555-CALL")]
    [InlineData("123456789012345678901")]
    [InlineData("+0123456789")]
    [InlineData("23055550100")]
    [InlineData("+1 809 555")]
    [InlineData("+1809555123")]
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
