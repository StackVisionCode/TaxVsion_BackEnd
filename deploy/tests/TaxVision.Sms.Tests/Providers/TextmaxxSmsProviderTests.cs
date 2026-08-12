using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Infrastructure.Providers;
using TaxVision.Sms.Infrastructure.Providers.Textmaxx;

namespace TaxVision.Sms.Tests.Providers;

public sealed class TextmaxxSmsProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpResponseMessage Response { get; set; } =
            new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"tmx-1\"}") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return Response;
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static SmsProviderConfig TextmaxxConfig() =>
        new()
        {
            BaseUrl = "https://api.textmaxx.test",
            SendPath = "send",
            HttpMethod = "POST",
            BodyFormat = "form",
            SenderId = "TAXV",
            Auth = new SmsAuthConfig { Type = "basic", Credential = "cli:tok" },
            RequestMap = new SmsRequestMap
            {
                To = "to",
                From = "from",
                Body = "message",
            },
            ResponseMap = new SmsResponseMap { ProviderMessageIdPath = "id" },
        };

    private static TextmaxxSmsProvider Build(CapturingHandler handler)
    {
        var options = Options.Create(new SmsProvidersOptions { Providers = { ["textmaxx"] = TextmaxxConfig() } });
        return new TextmaxxSmsProvider(
            new SingleClientFactory(handler),
            options,
            new HttpResiliencePipelineRegistry(),
            NullLogger<TextmaxxSmsProvider>.Instance
        );
    }

    [Fact]
    public void Capabilities_are_text_only_and_no_dlr()
    {
        var provider = Build(new CapturingHandler());

        Assert.Equal("textmaxx", provider.Code);
        Assert.False(provider.Capabilities.SupportsMedia);
        Assert.False(provider.Capabilities.SupportsDeliveryReceipts);
        Assert.True(provider.Capabilities.SupportsInbound);
        Assert.Equal(1, provider.Capabilities.MaxBatchSize);
    }

    [Fact]
    public async Task SendAsync_uses_basic_auth_base64_of_key_colon_token()
    {
        var handler = new CapturingHandler();
        var provider = Build(handler);

        var result = await provider.SendAsync(
            new SmsSendRequest(Guid.NewGuid(), Guid.NewGuid(), "+18095551234", "hola", [], "corr", "idem", null)
        );

        Assert.True(result.Value.Accepted);
        Assert.Equal("tmx-1", result.Value.ProviderMessageId);

        var auth = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("cli:tok")), auth.Parameter);
        Assert.Equal("https://api.textmaxx.test/send", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("message=hola", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_maps_provider_5xx_to_provider_unavailable()
    {
        var handler = new CapturingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("down"),
            },
        };
        var provider = Build(handler);

        var result = await provider.SendAsync(
            new SmsSendRequest(Guid.NewGuid(), Guid.NewGuid(), "+18095551234", "hola", [], "corr", "idem", null)
        );

        Assert.False(result.Value.Accepted);
        Assert.Equal("providerUnavailable", result.Value.ErrorCode);
    }

    [Theory]
    [InlineData("STOP", SmsInboundKeyword.Stop)]
    [InlineData("start", SmsInboundKeyword.Start)]
    [InlineData("Help", SmsInboundKeyword.Help)]
    [InlineData("hello", SmsInboundKeyword.Unknown)]
    public void ParseInbound_maps_keywords(string text, SmsInboundKeyword expected)
    {
        var provider = Build(new CapturingHandler());
        var payload = $"{{\"from\":\"+18095551234\",\"text\":\"{text}\"}}";

        var result = provider.ParseInbound(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Keyword);
        Assert.Equal("+18095551234", result.Value.FromPhone);
    }

    [Fact]
    public void VerifySignature_without_secret_is_invalid()
    {
        var provider = Build(new CapturingHandler());

        var result = provider.VerifySignature("{}", "whatever", string.Empty);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
    }

    [Fact]
    public void VerifySignature_accepts_matching_hmac()
    {
        var provider = Build(new CapturingHandler());
        const string secret = "s3cret";
        const string payload = "{\"a\":1}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        var result = provider.VerifySignature(payload, sig, secret);

        Assert.True(result.Value.IsValid);
    }
}
