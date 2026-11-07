using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Tests.Domain;

public sealed class OAuthScopeSetTests
{
    [Fact]
    public void Missing_ReturnsOnlyAbsentScopes()
    {
        var granted = OAuthScopeSet.Parse("Mail.Read  offline_access");

        var missing = granted.Missing(OAuthScopeSet.Parse("mail.read Mail.ReadWrite mail.readwrite"));

        Assert.Equal(["Mail.ReadWrite"], missing);
    }

    [Fact]
    public void Missing_FromNothing_ReturnsEveryRequiredScope() =>
        Assert.Equal(["a", "b"], OAuthScopeSet.Parse(null).Missing(OAuthScopeSet.Parse("a b")));

    [Fact]
    public void Contains_IgnoresCase()
    {
        var scopes = OAuthScopeSet.Parse("Mail.Read");

        Assert.True(scopes.Contains("mail.read"));
        Assert.False(scopes.Contains("Mail.Send"));
    }
}
