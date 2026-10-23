using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.Infrastructure.ClientForms;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class TurnstileFormIntakeBotProtectionTests
{
    [Fact]
    public async Task Missing_secret_fails_closed_without_calling_cloudflare()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessResponse());
        var protection = Create(handler, secretKey: null);

        Assert.False(protection.IsReady);
        Assert.False(await protection.VerifyAsync("token", "127.0.0.1"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Valid_response_requires_the_expected_action()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessResponse());
        var protection = Create(handler, "secret");

        var result = await protection.VerifyAsync("turnstile-token", "203.0.113.10");

        Assert.True(result);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("response=turnstile-token", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("remoteip=203.0.113.10", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("idempotency_key=", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Response_for_another_action_is_rejected()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"success\":true,\"action\":\"another_action\"}"
        );
        var protection = Create(handler, "secret");

        Assert.False(await protection.VerifyAsync("turnstile-token", "203.0.113.10"));
    }

    [Fact]
    public async Task Oversized_token_is_rejected_before_the_network_call()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, SuccessResponse());
        var protection = Create(handler, "secret");

        Assert.False(await protection.VerifyAsync(new string('x', 2049), "203.0.113.10"));
        Assert.Equal(0, handler.Calls);
    }

    private static TurnstileFormIntakeBotProtection Create(
        HttpMessageHandler handler,
        string? secretKey
    ) => new(
        new HttpClient(handler),
        Options.Create(
            new ClientFormsOptions
            {
                External = new ClientFormsExternalOptions
                {
                    BotProtection = new ClientFormsBotProtectionOptions
                    {
                        SecretKey = secretKey,
                    },
                },
            }
        )
    );

    private static string SuccessResponse() =>
        "{\"success\":true,\"action\":\"client_forms_intake\"}";

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
