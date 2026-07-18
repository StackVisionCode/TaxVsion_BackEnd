using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public class OAuthConnectionTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static OAuthConnection CreateValidConnection() =>
        OAuthConnection.Create(AccountId, ProviderCode.Gmail, "client-123", "gmail.readonly gmail.modify", Now).Value;

    private static OAuthToken CreateValidToken(Guid connectionId)
    {
        var accessCipher = EncryptedSecret.Create(new byte[] { 1, 2, 3 }, new byte[12], new byte[16], 1).Value;
        var refreshCipher = EncryptedSecret.Create(new byte[] { 4, 5, 6 }, new byte[12], new byte[16], 1).Value;
        return OAuthToken.Create(connectionId, accessCipher, refreshCipher, Now.AddHours(1), Now).Value;
    }

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = OAuthConnection.Create(AccountId, ProviderCode.Gmail, "client-123", "gmail.readonly", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OAuthConnectionStatus.Pending, result.Value.Status);
        Assert.Null(result.Value.Token);
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        var result = OAuthConnection.Create(Guid.Empty, ProviderCode.Gmail, "client-123", "gmail.readonly", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.AccountId", result.Error.Code);
    }

    [Fact]
    public void Create_WithEmptyClientId_Fails()
    {
        var result = OAuthConnection.Create(AccountId, ProviderCode.Gmail, "", "gmail.readonly", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.ClientId", result.Error.Code);
    }

    [Fact]
    public void Create_WithClientIdTooLong_Fails()
    {
        var result = OAuthConnection.Create(AccountId, ProviderCode.Gmail, new string('a', 201), "gmail.readonly", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.ClientId", result.Error.Code);
    }

    [Fact]
    public void Create_WithEmptyScope_Fails()
    {
        var result = OAuthConnection.Create(AccountId, ProviderCode.Gmail, "client-123", "", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.Scope", result.Error.Code);
    }

    [Fact]
    public void AttachToken_FromPending_ActivatesConnection()
    {
        var connection = CreateValidConnection();
        var token = CreateValidToken(connection.Id);

        var result = connection.AttachToken(token);

        Assert.True(result.IsSuccess);
        Assert.Equal(OAuthConnectionStatus.Active, connection.Status);
        Assert.Same(token, connection.Token);
    }

    [Fact]
    public void AttachToken_Twice_FailsOnSecondCall()
    {
        var connection = CreateValidConnection();
        connection.AttachToken(CreateValidToken(connection.Id));

        var result = connection.AttachToken(CreateValidToken(connection.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.TokenAlreadyAttached", result.Error.Code);
    }

    [Fact]
    public void MarkExpired_FromActive_Succeeds()
    {
        var connection = CreateValidConnection();
        connection.AttachToken(CreateValidToken(connection.Id));

        var result = connection.MarkExpired();

        Assert.True(result.IsSuccess);
        Assert.Equal(OAuthConnectionStatus.Expired, connection.Status);
    }

    [Fact]
    public void MarkExpired_FromPending_Fails()
    {
        var connection = CreateValidConnection();

        var result = connection.MarkExpired();

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Revoke_FromActive_Succeeds()
    {
        var connection = CreateValidConnection();
        connection.AttachToken(CreateValidToken(connection.Id));

        var result = connection.Revoke(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OAuthConnectionStatus.Revoked, connection.Status);
        Assert.Equal(Now, connection.RevokedAtUtc);
    }

    [Fact]
    public void Revoke_Twice_FailsOnSecondCall()
    {
        var connection = CreateValidConnection();
        connection.Revoke(Now);

        var result = connection.Revoke(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthConnection.InvalidTransition", result.Error.Code);
    }
}
