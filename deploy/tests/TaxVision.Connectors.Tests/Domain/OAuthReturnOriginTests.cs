using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public sealed class OAuthReturnOriginTests
{
    private const string ProductionBase = "https://app.taxoffix.io";
    private const string LocalBase = "http://localhost:4200";
    private const string Domain = "taxoffix.io";

    [Fact]
    public void TenantSubdomain_IsKeptAsTheOrigin()
    {
        var origin = OAuthReturnOrigin.Create("https://manfer.taxoffix.io/", ProductionBase, Domain);

        Assert.True(origin.IsSuccess);
        Assert.Equal("https://manfer.taxoffix.io", origin.Value.Value);
    }

    // En local no hay subdominio: el front vive en un puerto de localhost y se vuelve a ese puerto.
    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://localhost:4300")]
    [InlineData("http://127.0.0.1:4200")]
    public void LocalFrontendPort_IsAllowedWhenTheBaseUrlIsLocal(string raw)
    {
        var origin = OAuthReturnOrigin.Create(raw, LocalBase, "localhost");

        Assert.True(origin.IsSuccess);
        Assert.Equal(raw, origin.Value.Value);
    }

    [Fact]
    public void Localhost_IsRejectedInProduction()
    {
        var origin = OAuthReturnOrigin.Create("http://localhost:4200", ProductionBase, Domain);

        Assert.True(origin.IsFailure);
        Assert.Equal(OAuthReturnOrigin.NotAllowedCode, origin.Error.Code);
    }

    [Theory]
    [InlineData("https://manfer.taxoffix.io|1|1")]
    [InlineData("https://x|1|1@manfer.taxoffix.io")]
    [InlineData("https://user@manfer.taxoffix.io")]
    [InlineData("https://manfer.taxoffix.io/email")]
    [InlineData("https://manfer.taxoffix.io?next=/x")]
    [InlineData("https://evil.com")]
    [InlineData("https://eviltaxoffix.io")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void AnythingButAnAllowedOrigin_IsRejected(string raw)
    {
        var origin = OAuthReturnOrigin.Create(raw, ProductionBase, Domain);

        Assert.True(origin.IsFailure);
    }
}
