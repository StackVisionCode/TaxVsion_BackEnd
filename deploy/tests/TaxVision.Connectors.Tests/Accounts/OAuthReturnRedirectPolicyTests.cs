using TaxVision.Connectors.Application.Accounts;

namespace TaxVision.Connectors.Tests.Accounts;

/// <summary>
/// El callback de OAuth es anónimo y el <c>state</c> lo controla el usuario: si el origen de retorno
/// no se valida, es un open redirect. Estos tests fijan que solo hosts del propio dominio (o localhost)
/// se aceptan, que se descartan esquemas/hosts hostiles, y que jamás se filtra el path/query recibido.
/// </summary>
public sealed class OAuthReturnRedirectPolicyTests
{
    private const string ProdBase = "https://app.taxproffice.com";

    [Fact]
    public void Resolve_TenantSubdomain_ReturnsItWithoutPathOrQuery()
    {
        var result = OAuthReturnRedirectPolicy.Resolve("https://manfer.taxproffice.com/email?x=1", ProdBase);

        Assert.Equal("https://manfer.taxproffice.com", result);
    }

    [Fact]
    public void Resolve_ExactBaseHost_IsAllowed()
    {
        Assert.Equal(
            "https://app.taxproffice.com",
            OAuthReturnRedirectPolicy.Resolve("https://app.taxproffice.com", ProdBase)
        );
    }

    [Fact]
    public void Resolve_DeepSubdomain_IsAllowed()
    {
        Assert.Equal(
            "https://a.b.taxproffice.com",
            OAuthReturnRedirectPolicy.Resolve("https://a.b.taxproffice.com", ProdBase)
        );
    }

    [Theory]
    [InlineData("https://evil.com")] // dominio ajeno
    [InlineData("https://taxproffice.com.evil.com")] // confusión de sufijo (termina en .evil.com)
    [InlineData("https://eviltaxproffice.com")] // sin el "." previo no es subdominio
    [InlineData("http://localhost:4200")] // localhost NO permitido cuando el base es prod
    public void Resolve_HostileOrForeignHost_FallsBackToBase(string origin)
    {
        Assert.Equal(ProdBase, OAuthReturnRedirectPolicy.Resolve(origin, ProdBase));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://manfer.taxproffice.com")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_NonHttpOrMalformed_FallsBackToBase(string? origin)
    {
        Assert.Equal(ProdBase, OAuthReturnRedirectPolicy.Resolve(origin, ProdBase));
    }

    [Fact]
    public void Resolve_TrimsTrailingSlashOnFallback()
    {
        Assert.Equal(
            "https://app.taxproffice.com",
            OAuthReturnRedirectPolicy.Resolve(null, "https://app.taxproffice.com/")
        );
    }

    [Fact]
    public void Resolve_LocalhostAllowed_WhenBaseIsLocalhost()
    {
        // Dev: base y retorno son localhost (puertos distintos permitidos: se compara host, no puerto).
        Assert.Equal(
            "http://localhost:4200",
            OAuthReturnRedirectPolicy.Resolve("http://localhost:4200", "http://localhost:5047")
        );
    }
}
