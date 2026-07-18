using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public class EncryptedSecretTests
{
    [Fact]
    public void Create_WithValidParts_Succeeds()
    {
        var result = EncryptedSecret.Create(new byte[] { 1, 2, 3 }, new byte[12], new byte[16], 1);

        Assert.True(result.IsSuccess);
        Assert.Equal((short)1, result.Value.KeyVersion);
    }

    [Fact]
    public void Create_WithEmptyCiphertext_Fails()
    {
        var result = EncryptedSecret.Create([], new byte[12], new byte[16], 1);

        Assert.True(result.IsFailure);
        Assert.Equal("EncryptedSecret.EmptyCiphertext", result.Error.Code);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(16)]
    public void Create_WithWrongNonceLength_Fails(int nonceLength)
    {
        var result = EncryptedSecret.Create(new byte[] { 1 }, new byte[nonceLength], new byte[16], 1);

        Assert.True(result.IsFailure);
        Assert.Equal("EncryptedSecret.InvalidNonce", result.Error.Code);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(17)]
    public void Create_WithWrongTagLength_Fails(int tagLength)
    {
        var result = EncryptedSecret.Create(new byte[] { 1 }, new byte[12], new byte[tagLength], 1);

        Assert.True(result.IsFailure);
        Assert.Equal("EncryptedSecret.InvalidTag", result.Error.Code);
    }

    [Fact]
    public void Create_WithNonPositiveKeyVersion_Fails()
    {
        var result = EncryptedSecret.Create(new byte[] { 1 }, new byte[12], new byte[16], 0);

        Assert.True(result.IsFailure);
        Assert.Equal("EncryptedSecret.InvalidKeyVersion", result.Error.Code);
    }

    [Fact]
    public void Equals_WithSameParts_ReturnsTrue()
    {
        var a = EncryptedSecret.Create(new byte[] { 1, 2 }, new byte[12], new byte[16], 1).Value;
        var b = EncryptedSecret.Create(new byte[] { 1, 2 }, new byte[12], new byte[16], 1).Value;

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_WithDifferentKeyVersion_ReturnsFalse()
    {
        var a = EncryptedSecret.Create(new byte[] { 1, 2 }, new byte[12], new byte[16], 1).Value;
        var b = EncryptedSecret.Create(new byte[] { 1, 2 }, new byte[12], new byte[16], 2).Value;

        Assert.NotEqual(a, b);
    }
}
