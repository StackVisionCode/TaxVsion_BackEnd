using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public class SmtpCredentialsTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static EncryptedSecret ValidSecret() =>
        EncryptedSecret.Create(new byte[] { 1 }, new byte[12], new byte[16], 1).Value;

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = SmtpCredentials.Create(
            AccountId,
            "smtp.example.com",
            587,
            true,
            "user@example.com",
            ValidSecret()
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.Value.AccountId);
        Assert.Equal("smtp.example.com", result.Value.Host);
        Assert.Equal(587, result.Value.Port);
        Assert.True(result.Value.UseStartTls);
        Assert.Equal("user@example.com", result.Value.Username);
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        var result = SmtpCredentials.Create(
            Guid.Empty,
            "smtp.example.com",
            587,
            true,
            "user@example.com",
            ValidSecret()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.AccountId", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankHost_Fails(string host)
    {
        var result = SmtpCredentials.Create(AccountId, host, 587, true, "user@example.com", ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.Host", result.Error.Code);
    }

    [Fact]
    public void Create_WithHostTooLong_Fails()
    {
        var host = new string('h', 256);

        var result = SmtpCredentials.Create(AccountId, host, 587, true, "user@example.com", ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.Host", result.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Create_WithInvalidPort_Fails(int port)
    {
        var result = SmtpCredentials.Create(
            AccountId,
            "smtp.example.com",
            port,
            true,
            "user@example.com",
            ValidSecret()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.Port", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankUsername_Fails(string username)
    {
        var result = SmtpCredentials.Create(AccountId, "smtp.example.com", 587, true, username, ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.Username", result.Error.Code);
    }

    [Fact]
    public void Create_WithUsernameTooLong_Fails()
    {
        var username = new string('u', 321);

        var result = SmtpCredentials.Create(AccountId, "smtp.example.com", 587, true, username, ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.Username", result.Error.Code);
    }

    [Fact]
    public void Create_WithNullPasswordCipher_Fails()
    {
        var result = SmtpCredentials.Create(AccountId, "smtp.example.com", 587, true, "user@example.com", null!);

        Assert.True(result.IsFailure);
        Assert.Equal("SmtpCredentials.PasswordCipher", result.Error.Code);
    }

    [Fact]
    public void UpdatePassword_ReplacesPasswordCipher()
    {
        var credentials = SmtpCredentials
            .Create(AccountId, "smtp.example.com", 587, true, "user@example.com", ValidSecret())
            .Value;
        var newSecret = EncryptedSecret.Create(new byte[] { 9 }, new byte[12], new byte[16], 2).Value;

        credentials.UpdatePassword(newSecret);

        Assert.Same(newSecret, credentials.PasswordCipher);
    }
}
