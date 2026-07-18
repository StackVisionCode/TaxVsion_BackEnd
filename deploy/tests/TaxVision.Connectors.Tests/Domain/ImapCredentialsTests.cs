using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public class ImapCredentialsTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static EncryptedSecret ValidSecret() =>
        EncryptedSecret.Create(new byte[] { 1 }, new byte[12], new byte[16], 1).Value;

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = ImapCredentials.Create(
            AccountId,
            "imap.example.com",
            993,
            true,
            "user@example.com",
            ValidSecret()
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountId, result.Value.AccountId);
        Assert.Equal("imap.example.com", result.Value.Host);
        Assert.Equal(993, result.Value.Port);
        Assert.True(result.Value.UseSsl);
        Assert.Equal("user@example.com", result.Value.Username);
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        var result = ImapCredentials.Create(
            Guid.Empty,
            "imap.example.com",
            993,
            true,
            "user@example.com",
            ValidSecret()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.AccountId", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankHost_Fails(string host)
    {
        var result = ImapCredentials.Create(AccountId, host, 993, true, "user@example.com", ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.Host", result.Error.Code);
    }

    [Fact]
    public void Create_WithHostTooLong_Fails()
    {
        var host = new string('h', 256);

        var result = ImapCredentials.Create(AccountId, host, 993, true, "user@example.com", ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.Host", result.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Create_WithInvalidPort_Fails(int port)
    {
        var result = ImapCredentials.Create(
            AccountId,
            "imap.example.com",
            port,
            true,
            "user@example.com",
            ValidSecret()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.Port", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankUsername_Fails(string username)
    {
        var result = ImapCredentials.Create(AccountId, "imap.example.com", 993, true, username, ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.Username", result.Error.Code);
    }

    [Fact]
    public void Create_WithUsernameTooLong_Fails()
    {
        var username = new string('u', 321);

        var result = ImapCredentials.Create(AccountId, "imap.example.com", 993, true, username, ValidSecret());

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.Username", result.Error.Code);
    }

    [Fact]
    public void Create_WithNullPasswordCipher_Fails()
    {
        var result = ImapCredentials.Create(AccountId, "imap.example.com", 993, true, "user@example.com", null!);

        Assert.True(result.IsFailure);
        Assert.Equal("ImapCredentials.PasswordCipher", result.Error.Code);
    }

    [Fact]
    public void UpdatePassword_ReplacesPasswordCipher()
    {
        var credentials = ImapCredentials
            .Create(AccountId, "imap.example.com", 993, true, "user@example.com", ValidSecret())
            .Value;
        var newSecret = EncryptedSecret.Create(new byte[] { 9 }, new byte[12], new byte[16], 2).Value;

        credentials.UpdatePassword(newSecret);

        Assert.Same(newSecret, credentials.PasswordCipher);
    }
}
