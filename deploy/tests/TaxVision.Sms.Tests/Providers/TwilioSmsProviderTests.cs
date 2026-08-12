using System.Net;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Infrastructure.Providers;
using TaxVision.Sms.Infrastructure.Providers.Twilio;

namespace TaxVision.Sms.Tests.Providers;

public sealed class TwilioSmsProviderTests
{
    private const string AccountSid = "AC123";
    private const string AuthToken = "mytoken";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpResponseMessage Response { get; set; } =
            new(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"sid\":\"SM123\",\"status\":\"queued\",\"error_code\":null}"),
            };

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

    private static TwilioSmsProvider Build(CapturingHandler handler)
    {
        var config = new SmsProviderConfig
        {
            BaseUrl = "https://api.twilio.test",
            SenderId = "+15550001111",
            Auth = new SmsAuthConfig { Type = "basic", Credential = $"{AccountSid}:{AuthToken}" },
        };
        var options = Options.Create(new SmsProvidersOptions { Providers = { ["twilio"] = config } });
        return new TwilioSmsProvider(
            new SingleClientFactory(handler),
            options,
            new HttpResiliencePipelineRegistry(),
            NullLogger<TwilioSmsProvider>.Instance
        );
    }

    private static SmsSendRequest Send(params SmsMediaPayload[] media) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "+18095550000", "hola", media, "corr", "idem", null);

    [Fact]
    public void Capabilities_support_mms_dlr_and_inbound()
    {
        var provider = Build(new CapturingHandler());

        Assert.Equal("twilio", provider.Code);
        Assert.True(provider.Capabilities.SupportsMedia); // MMS
        Assert.True(provider.Capabilities.SupportsDeliveryReceipts);
        Assert.True(provider.Capabilities.SupportsInbound);
        Assert.False(provider.Capabilities.SupportsBulkSend);
    }

    [Fact]
    public async Task SendAsync_uses_basic_auth_account_path_and_form_body()
    {
        var handler = new CapturingHandler();
        var provider = Build(handler);

        var result = await provider.SendAsync(Send());

        Assert.True(result.Value.Accepted);
        Assert.Equal("SM123", result.Value.ProviderMessageId);

        var auth = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AccountSid}:{AuthToken}")), auth.Parameter);
        Assert.Equal("https://api.twilio.test/2010-04-01/Accounts/AC123/Messages.json", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("Body=hola", handler.LastBody);
        Assert.Contains("To=", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_includes_media_url_for_mms()
    {
        var handler = new CapturingHandler();
        var provider = Build(handler);

        await provider.SendAsync(Send(new SmsMediaPayload("https://m/x.jpg", "image/jpeg", null, 1000)));

        Assert.Contains("MediaUrl", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_maps_http_error_to_provider_code()
    {
        var handler = new CapturingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"code\":21211,\"message\":\"Invalid 'To'\",\"status\":400}"),
            },
        };
        var provider = Build(handler);

        var result = await provider.SendAsync(Send());

        Assert.False(result.Value.Accepted);
        Assert.Equal("21211", result.Value.ErrorCode);
    }

    [Theory]
    [InlineData("delivered", SmsCanonicalStatus.Delivered)]
    [InlineData("undelivered", SmsCanonicalStatus.Undeliverable)]
    [InlineData("failed", SmsCanonicalStatus.Failed)]
    [InlineData("sent", SmsCanonicalStatus.Accepted)]
    public void ParseDeliveryReceipt_reads_form_encoded_status(string status, SmsCanonicalStatus expected)
    {
        var provider = Build(new CapturingHandler());
        var body = $"MessageSid=SM123&MessageStatus={status}&To=%2B18095550000";

        var result = provider.ParseDeliveryReceipt(body);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Status);
        Assert.Equal("SM123", result.Value.ProviderMessageId);
    }

    [Fact]
    public void ParseInbound_reads_form_encoded_from_and_body()
    {
        var provider = Build(new CapturingHandler());
        var body = "From=%2B18095551234&To=%2B15550001111&Body=STOP&MessageSid=SM9";

        var result = provider.ParseInbound(body);

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsInboundKeyword.Stop, result.Value.Keyword);
        Assert.Equal("+18095551234", result.Value.FromPhone);
    }

    [Fact]
    public void VerifySignature_validates_real_twilio_scheme()
    {
        var provider = Build(new CapturingHandler());
        const string url = "https://x.test/sms/webhooks/twilio/status";
        const string body = "Body=hi&From=%2B18095551234&To=%2B18095550000";
        // Twilio: HMAC-SHA1(authToken, url + Σ(key+value) ordenados por clave), base64.
        // Parsed → Body=hi, From=+18095551234, To=+18095550000 ; orden: Body, From, To.
        var data = url + "Bodyhi" + "From+18095551234" + "To+18095550000";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(AuthToken));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));

        var ok = provider.VerifySignature(body, sig, "", url);
        Assert.True(ok.Value.IsValid);

        var wrong = provider.VerifySignature(body, "not-the-sig", "", url);
        Assert.False(wrong.Value.IsValid);

        var noUrl = provider.VerifySignature(body, sig, "", "");
        Assert.False(noUrl.Value.IsValid);
    }
}
