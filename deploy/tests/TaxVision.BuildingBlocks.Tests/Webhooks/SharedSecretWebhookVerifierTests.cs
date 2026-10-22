using BuildingBlocks.Web.Webhooks;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Webhooks;

public class SharedSecretWebhookVerifierTests
{
    private const string Secret = "configured-secret";

    private static HttpRequest RequestWith(string? bearer = null, string? header = null, string? query = null)
    {
        var context = new DefaultHttpContext();
        if (bearer is not null)
            context.Request.Headers.Authorization = $"Bearer {bearer}";
        if (header is not null)
            context.Request.Headers[SharedSecretWebhookVerifier.HeaderName] = header;
        if (query is not null)
            context.Request.QueryString = new QueryString($"?key={query}");
        return context.Request;
    }

    [Fact]
    public void Verify_WithBearer_Accepts()
    {
        var check = SharedSecretWebhookVerifier.Verify(RequestWith(bearer: Secret), Secret);

        Assert.Equal(WebhookSecretVerdict.Accepted, check.Verdict);
        Assert.Equal(WebhookSecretSource.Bearer, check.Source);
    }

    [Fact]
    public void Verify_WithHeader_Accepts()
    {
        var check = SharedSecretWebhookVerifier.Verify(RequestWith(header: Secret), Secret);

        Assert.Equal(WebhookSecretVerdict.Accepted, check.Verdict);
        Assert.Equal(WebhookSecretSource.Header, check.Source);
    }

    [Fact]
    public void Verify_WithLegacyQuery_AcceptsAndReportsTheSource()
    {
        var check = SharedSecretWebhookVerifier.Verify(RequestWith(query: Secret), Secret);

        Assert.Equal(WebhookSecretVerdict.Accepted, check.Verdict);
        // El caller usa la fuente para avisar en el log que se pase a Bearer.
        Assert.Equal(WebhookSecretSource.Query, check.Source);
    }

    [Fact]
    public void Verify_BearerWinsOverHeaderAndQuery()
    {
        var check = SharedSecretWebhookVerifier.Verify(
            RequestWith(bearer: "wrong", header: Secret, query: Secret),
            Secret
        );

        Assert.Equal(WebhookSecretVerdict.Rejected, check.Verdict);
        Assert.Equal(WebhookSecretSource.Bearer, check.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_WithoutConfiguredSecret_FailsClosed(string? configured)
    {
        var check = SharedSecretWebhookVerifier.Verify(RequestWith(bearer: "anything"), configured);

        Assert.Equal(WebhookSecretVerdict.NotConfigured, check.Verdict);
    }

    [Fact]
    public void Verify_WithNoSecretSent_Rejects()
    {
        var check = SharedSecretWebhookVerifier.Verify(RequestWith(), Secret);

        Assert.Equal(WebhookSecretVerdict.Rejected, check.Verdict);
        Assert.Equal(WebhookSecretSource.None, check.Source);
    }

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "different", false)]
    [InlineData("secret", "secret ", false)]
    [InlineData("secret", "SECRET", false)]
    [InlineData("secret", null, false)]
    public void Matches_IsExact(string expected, string? provided, bool matches)
    {
        Assert.Equal(matches, SharedSecretWebhookVerifier.Matches(expected, provided));
    }
}
