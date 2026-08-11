using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Infrastructure.Providers;
using TaxVision.Sms.Infrastructure.Providers.Infobip;

namespace TaxVision.Sms.Tests.Providers;

public sealed class InfobipSmsProviderTests
{
    private const string ApiKey = "ib-secret-3fb4";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpResponseMessage Response { get; set; } =
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"messages\":[{\"to\":\"+18095551234\",\"status\":{\"groupId\":1,\"groupName\":\"PENDING\",\"name\":\"PENDING_ACCEPTED\"},\"messageId\":\"ibx-1\"}]}"
                ),
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

    private static InfobipSmsProvider Build(CapturingHandler handler)
    {
        var config = new SmsProviderConfig
        {
            BaseUrl = "https://vyg8je.api.infobip.test",
            SendPath = "/sms/2/text/advanced",
            SenderId = "InfoSMS",
            Auth = new SmsAuthConfig { Type = "app", Credential = ApiKey },
        };
        var options = Options.Create(new SmsProvidersOptions { Providers = { ["infobip"] = config } });
        return new InfobipSmsProvider(
            new SingleClientFactory(handler),
            options,
            new HttpResiliencePipelineRegistry(),
            NullLogger<InfobipSmsProvider>.Instance
        );
    }

    private static SmsSendRequest Send(string to = "+18095551234", string body = "hola") =>
        new(Guid.NewGuid(), Guid.NewGuid(), to, body, [], "corr", "idem", null);

    [Fact]
    public void Capabilities_support_dlr_inbound_bulk_but_not_media()
    {
        var provider = Build(new CapturingHandler());

        Assert.Equal("infobip", provider.Code);
        Assert.True(provider.Capabilities.SupportsDeliveryReceipts);
        Assert.True(provider.Capabilities.SupportsInbound);
        Assert.True(provider.Capabilities.SupportsBulkSend);
        Assert.False(provider.Capabilities.SupportsMedia);
    }

    [Fact]
    public async Task SendAsync_uses_App_auth_nested_body_and_parses_messageId()
    {
        var handler = new CapturingHandler();
        var provider = Build(handler);

        var result = await provider.SendAsync(Send());

        Assert.True(result.Value.Accepted);
        Assert.Equal("ibx-1", result.Value.ProviderMessageId);

        var auth = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("App", auth.Scheme);
        Assert.Equal(ApiKey, auth.Parameter);
        Assert.Equal("https://vyg8je.api.infobip.test/sms/2/text/advanced", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"destinations\"", handler.LastBody);
        Assert.Contains("+18095551234", handler.LastBody);
        Assert.Contains("hola", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_treats_rejected_group_as_not_accepted()
    {
        var handler = new CapturingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"messages\":[{\"status\":{\"groupId\":5,\"groupName\":\"REJECTED\"},\"messageId\":\"ibx-2\"}]}"
                ),
            },
        };
        var provider = Build(handler);

        var result = await provider.SendAsync(Send());

        Assert.False(result.Value.Accepted);
        Assert.Equal("providerRejected", result.Value.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_maps_5xx_to_provider_unavailable()
    {
        var handler = new CapturingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("err") },
        };
        var provider = Build(handler);

        var result = await provider.SendAsync(Send());

        Assert.False(result.Value.Accepted);
        Assert.Equal("providerUnavailable", result.Value.ErrorCode);
    }

    [Theory]
    [InlineData("DELIVERED", SmsCanonicalStatus.Delivered)]
    [InlineData("UNDELIVERABLE", SmsCanonicalStatus.Undeliverable)]
    [InlineData("REJECTED", SmsCanonicalStatus.Failed)]
    [InlineData("PENDING", SmsCanonicalStatus.Accepted)]
    public void ParseDeliveryReceipt_maps_group_names(string groupName, SmsCanonicalStatus expected)
    {
        var provider = Build(new CapturingHandler());
        var payload =
            $"{{\"results\":[{{\"messageId\":\"ibx-1\",\"status\":{{\"groupName\":\"{groupName}\",\"name\":\"X\"}}}}]}}";

        var result = provider.ParseDeliveryReceipt(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Status);
        Assert.Equal("ibx-1", result.Value.ProviderMessageId);
    }

    [Fact]
    public void ParseInbound_reads_from_and_keyword()
    {
        var provider = Build(new CapturingHandler());
        var payload = "{\"results\":[{\"from\":\"+18095551234\",\"to\":\"12345\",\"text\":\"STOP\",\"messageId\":\"mo-1\"}]}";

        var result = provider.ParseInbound(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsInboundKeyword.Stop, result.Value.Keyword);
        Assert.Equal("+18095551234", result.Value.FromPhone);
    }

    [Fact]
    public void VerifySignature_without_secret_is_invalid()
    {
        var provider = Build(new CapturingHandler());

        var result = provider.VerifySignature("{}", "sig", string.Empty);

        Assert.False(result.Value.IsValid);
    }
}
