using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Providers.Graph;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Tests.Providers;

public class GraphApiClientTests
{
    private static GraphApiClient CreateClient(
        FakeHttpMessageHandler handler,
        NoWaitProviderRateLimiter? rateLimiter = null
    ) =>
        new(
            new HttpClient(handler),
            new FakeOAuthTokenManager(),
            rateLimiter ?? new NoWaitProviderRateLimiter(),
            new ProviderCircuitBreakerRegistry(NullLogger<ProviderCircuitBreakerRegistry>.Instance),
            NullLogger<GraphApiClient>.Instance
        );

    [Fact]
    public async Task GetHistoryAsync_UsesInboxScopedDeltaAndParsesDeltaLink()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "value": [ { "id": "msg1" } ],
              "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=abc"
            }
            """
        );

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), null);

        Assert.Single(result.NewMessageIds);
        Assert.Equal("msg1", result.NewMessageIds[0]);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=abc",
            result.NextCursor
        );
        Assert.False(result.HasMore);
        Assert.Contains("/mailFolders/inbox/messages/delta", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetHistoryAsync_WithNextLink_FollowsPaginationUntilDeltaLink()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ { "id": "msg1" } ], "@odata.nextLink": "https://graph.microsoft.com/v1.0/next-page" }"""
        );
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ { "id": "msg2" } ], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/delta-final" }"""
        );

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), null);

        Assert.Equal(["msg1", "msg2"], result.NewMessageIds);
        Assert.Equal("https://graph.microsoft.com/v1.0/delta-final", result.NextCursor);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetMessageAsync_ParsesStructuredFieldsHeadersAndAttachmentMetadata()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "id": "msg1",
              "conversationId": "conv1",
              "subject": "Tax question",
              "from": {"emailAddress":{"address":"customer@example.com"}},
              "toRecipients": [{"emailAddress":{"address":"office@outlook.com"}}],
              "receivedDateTime": "2026-01-01T00:00:00Z",
              "hasAttachments": true,
              "internetMessageId": "<xyz@example.com>",
              "internetMessageHeaders": [
                {"name":"Authentication-Results","value":"spf=pass; dkim=pass; dmarc=pass"},
                {"name":"In-Reply-To","value":"<parent@example.com>"}
              ],
              "bodyPreview": "Hello"
            }
            """
        );
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ {"id":"att1","name":"doc.pdf","contentType":"application/pdf","size":2048} ] }"""
        );

        var message = await CreateClient(handler).GetMessageAsync(Guid.NewGuid(), "msg1");

        Assert.Equal("msg1", message.ProviderMessageId);
        Assert.Equal("conv1", message.ProviderThreadId);
        Assert.Equal("customer@example.com", message.From);
        Assert.Equal(["office@outlook.com"], message.To);
        Assert.Equal("<xyz@example.com>", message.InternetMessageId);
        Assert.Equal("<parent@example.com>", message.InReplyTo);
        Assert.True(message.HasAttachments);
        Assert.Single(message.Attachments);
        Assert.Equal("att1", message.Attachments[0].ProviderAttachmentId);
        Assert.Equal(AuthenticationResult.Pass, message.AuthenticationSignals.DmarcResult);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAttachmentAsync_DecodesStandardBase64Payload()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "contentBytes": "SGkh" }""");

        await using var stream = await CreateClient(handler).GetAttachmentAsync(Guid.NewGuid(), "msg1", "att1");
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Equal("Hi!", content);
    }

    [Fact]
    public async Task SendAsync_On429_WaitsRetryAfterAndRetriesOnce()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromMilliseconds(50)
            );
            return response;
        });
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/final" }"""
        );

        var rateLimiter = new NoWaitProviderRateLimiter();
        var result = await CreateClient(handler, rateLimiter).GetHistoryAsync(Guid.NewGuid(), null);

        Assert.Equal("https://graph.microsoft.com/v1.0/final", result.NextCursor);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(rateLimiter.RecordedRateLimits);
        Assert.Equal(ProviderCode.Graph, rateLimiter.RecordedRateLimits[0].Provider);
    }

    [Fact]
    public async Task GetMessageBodyAsync_WithHtmlContentType_MapsToHtmlBodyAndApproximatesMimeSize()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "body": {"contentType":"html","content":"<p>Hi!</p>"},
              "internetMessageHeaders": [{"name":"Subject","value":"Tax question"}],
              "hasAttachments": true
            }
            """
        );
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ {"id":"att1","name":"doc.pdf","contentType":"application/pdf","size":2048} ] }"""
        );

        var body = await CreateClient(handler).GetMessageBodyAsync(Guid.NewGuid(), "msg1");

        Assert.Equal("<p>Hi!</p>", body.HtmlBody);
        Assert.Null(body.TextBody);
        Assert.Equal("Tax question", body.Headers["Subject"]);
        Assert.Single(body.Attachments);
        Assert.True(body.MimeSizeBytes > 0);
    }

    [Fact]
    public async Task GetMessageBodyAsync_WithTextContentType_MapsToTextBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "body": {"contentType":"text","content":"Hi!"} }""");

        var body = await CreateClient(handler).GetMessageBodyAsync(Guid.NewGuid(), "msg1");

        Assert.Equal("Hi!", body.TextBody);
        Assert.Null(body.HtmlBody);
    }

    private static OutboundMessage NewOutboundMessage(string? replyToProviderMessageId = null) =>
        new("Subject", "<p>Html</p>", "Text", ["to@example.com"], [], [], null, null, null, replyToProviderMessageId);

    [Fact]
    public async Task SendMessageAsync_NewMessage_PostsSendMailWithRecipients()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        var result = await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage());

        Assert.Null(result.ProviderMessageId);
        Assert.Contains("/sendMail", handler.Requests[0].RequestUri!.ToString());

        using var payload = JsonDocument.Parse(capturedBody!);
        var toAddress = payload
            .RootElement.GetProperty("message")
            .GetProperty("toRecipients")[0]
            .GetProperty("emailAddress")
            .GetProperty("address")
            .GetString();
        Assert.Equal("to@example.com", toAddress);
    }

    [Fact]
    public async Task SendMessageAsync_Reply_PostsOneStepReplyWithHtmlPreferHeader()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage("original1"));

        Assert.Contains("/messages/original1/reply", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("outlook.body-content-type=\"html\"", handler.Requests[0].Headers.GetValues("Prefer").Single());
    }

    [Fact]
    public async Task SendMessageAsync_On401_ThrowsAuthExpired()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage())
        );

        Assert.Equal(SendFailureReason.AuthExpired, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_On429_ThrowsQuotaExceeded()
    {
        var handler = new FakeHttpMessageHandler();
        HttpResponseMessage TooManyRequestsWithShortRetryAfter(HttpRequestMessage _)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromMilliseconds(50)
            );
            return response;
        }
        handler.Enqueue(TooManyRequestsWithShortRetryAfter);
        handler.Enqueue(TooManyRequestsWithShortRetryAfter);

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler, new NoWaitProviderRateLimiter())
                .SendMessageAsync(Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage())
        );

        Assert.Equal(SendFailureReason.QuotaExceeded, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_WithAttachmentUnder3Mb_IncludesItInSendMailRequest()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var message = new OutboundMessage(
            "Subject",
            "<p>Html</p>",
            "Text",
            ["to@example.com"],
            [],
            [],
            null,
            null,
            null,
            null,
            [new OutboundAttachment("doc.pdf", "application/pdf", "%PDF-1.4 fake"u8.ToArray())]
        );

        await CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@outlook.com", null, message);

        using var payload = JsonDocument.Parse(capturedBody!);
        var attachment = payload.RootElement.GetProperty("message").GetProperty("attachments")[0];
        Assert.Equal("doc.pdf", attachment.GetProperty("name").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("contentType").GetString());
        Assert.Equal("#microsoft.graph.fileAttachment", attachment.GetProperty("@odata.type").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_WithAttachmentsOver3Mb_ThrowsAttachmentTooLargeWithoutCallingGraph()
    {
        var handler = new FakeHttpMessageHandler();
        var oversized = new byte[3 * 1024 * 1024 + 1];
        var message = new OutboundMessage(
            "Subject",
            "<p>Html</p>",
            "Text",
            ["to@example.com"],
            [],
            [],
            null,
            null,
            null,
            null,
            [new OutboundAttachment("big.bin", "application/octet-stream", oversized)]
        );

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@outlook.com", null, message)
        );

        Assert.Equal(SendFailureReason.AttachmentTooLarge, exception.Reason);
        Assert.Empty(handler.Requests);
    }
}
