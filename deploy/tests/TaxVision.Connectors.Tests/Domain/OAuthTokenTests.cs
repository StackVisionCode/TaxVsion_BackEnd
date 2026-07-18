using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public class OAuthTokenTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static EncryptedSecret ValidSecret(byte firstByte) =>
        EncryptedSecret.Create(new[] { firstByte }, new byte[12], new byte[16], 1).Value;

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var access = ValidSecret(1);
        var refresh = ValidSecret(2);

        var result = OAuthToken.Create(ConnectionId, access, refresh, Now.AddHours(1), Now);

        Assert.True(result.IsSuccess);
        Assert.Same(access, result.Value.AccessTokenCipher);
        Assert.Same(refresh, result.Value.RefreshTokenCipher);
    }

    [Fact]
    public void Create_WithEmptyConnectionId_Fails()
    {
        var result = OAuthToken.Create(Guid.Empty, ValidSecret(1), ValidSecret(2), Now.AddHours(1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("OAuthToken.ConnectionId", result.Error.Code);
    }

    [Fact]
    public void UpdateAccessToken_ReplacesAccessTokenAndTimestamps()
    {
        var token = OAuthToken.Create(ConnectionId, ValidSecret(1), ValidSecret(2), Now.AddHours(1), Now).Value;
        var newAccess = ValidSecret(9);
        var newExpiry = Now.AddHours(2);
        var refreshedAt = Now.AddMinutes(50);

        token.UpdateAccessToken(newAccess, newExpiry, refreshedAt);

        Assert.Same(newAccess, token.AccessTokenCipher);
        Assert.Equal(newExpiry, token.AccessTokenExpiresAtUtc);
        Assert.Equal(refreshedAt, token.RefreshedAtUtc);
    }

    [Fact]
    public void UpdateRefreshToken_ReplacesRefreshTokenOnly()
    {
        var token = OAuthToken.Create(ConnectionId, ValidSecret(1), ValidSecret(2), Now.AddHours(1), Now).Value;
        var originalAccess = token.AccessTokenCipher;
        var newRefresh = ValidSecret(9);

        token.UpdateRefreshToken(newRefresh);

        Assert.Same(newRefresh, token.RefreshTokenCipher);
        Assert.Same(originalAccess, token.AccessTokenCipher);
    }
}
