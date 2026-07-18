using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Providers.Gmail;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Tests.Providers;

public class GmailApiClientTests
{
    private static GmailApiClient CreateClient(
        FakeHttpMessageHandler handler,
        NoWaitProviderRateLimiter? rateLimiter = null
    ) =>
        new(
            new HttpClient(handler),
            new FakeOAuthTokenManager(),
            rateLimiter ?? new NoWaitProviderRateLimiter(),
            new ProviderCircuitBreakerRegistry(NullLogger<ProviderCircuitBreakerRegistry>.Instance),
            NullLogger<GmailApiClient>.Instance
        );

    [Fact]
    public async Task GetHistoryAsync_ParsesMessagesAddedAndHistoryId()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "history": [
                { "id": "100", "messagesAdded": [ { "message": { "id": "msg1", "threadId": "thread1", "labelIds": ["INBOX"] } } ] }
              ],
              "historyId": "101"
            }
            """
        );

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), "50");

        Assert.Single(result.NewMessageIds);
        Assert.Equal("msg1", result.NewMessageIds[0]);
        Assert.Equal("101", result.NextCursor);
        Assert.False(result.HasMore);
        Assert.Contains("labelId=INBOX", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("startHistoryId=50", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetMessageAsync_ParsesHeadersAttachmentsAndAuthenticationSignals()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "id": "msg1",
              "threadId": "thread1",
              "snippet": "Hello there",
              "internalDate": "1700000000000",
              "payload": {
                "headers": [
                  {"name":"From","value":"customer@example.com"},
                  {"name":"To","value":"office@gmail.com"},
                  {"name":"Subject","value":"Tax question"},
                  {"name":"Message-ID","value":"<abc@example.com>"},
                  {"name":"Authentication-Results","value":"mx.google.com; spf=pass smtp.mailfrom=customer@example.com; dkim=pass header.i=@example.com; dmarc=fail"}
                ],
                "parts": [
                  {"filename":"doc.pdf","mimeType":"application/pdf","body":{"attachmentId":"att1","size":2048}}
                ]
              }
            }
            """
        );

        var message = await CreateClient(handler).GetMessageAsync(Guid.NewGuid(), "msg1");

        Assert.Equal("msg1", message.ProviderMessageId);
        Assert.Equal("thread1", message.ProviderThreadId);
        Assert.Equal("customer@example.com", message.From);
        Assert.Equal("Tax question", message.Subject);
        Assert.Equal("<abc@example.com>", message.InternetMessageId);
        Assert.True(message.HasAttachments);
        Assert.Single(message.Attachments);
        Assert.Equal("att1", message.Attachments[0].ProviderAttachmentId);
        Assert.Equal(AuthenticationResult.Pass, message.AuthenticationSignals.SpfResult);
        Assert.Equal(AuthenticationResult.Pass, message.AuthenticationSignals.DkimResult);
        Assert.Equal(AuthenticationResult.Fail, message.AuthenticationSignals.DmarcResult);
        Assert.Contains("metadataHeaders=Authentication-Results", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAttachmentAsync_DecodesBase64UrlPayload()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{ "size": 3, "data": "SGkh" }""");

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
        handler.Enqueue(HttpStatusCode.OK, """{ "history": [], "historyId": "1" }""");

        var rateLimiter = new NoWaitProviderRateLimiter();
        var result = await CreateClient(handler, rateLimiter).GetHistoryAsync(Guid.NewGuid(), null);

        Assert.Equal("1", result.NextCursor);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(rateLimiter.RecordedRateLimits);
        Assert.Equal(ProviderCode.Gmail, rateLimiter.RecordedRateLimits[0].Provider);
    }

    [Fact]
    public async Task SendAsync_OnPersistentFailure_ThrowsEmailProviderException()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<TaxVision.Connectors.Application.Providers.EmailProviderException>(() =>
            CreateClient(handler).GetMessageAsync(Guid.NewGuid(), "msg1")
        );
    }

    [Fact]
    public async Task SendAsync_WithTransientNetworkFailureThenSuccess_RetriesViaCircuitBreakerAndSucceeds()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => throw new HttpRequestException("simulated network blip"));
        handler.Enqueue(HttpStatusCode.OK, """{ "history": [], "historyId": "1" }""");

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), null);

        Assert.Equal("1", result.NextCursor);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetMessageBodyAsync_WalksMultipartTreeAndExtractsHtmlTextAndAttachments()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sizeEstimate": 4096,
              "payload": {
                "mimeType": "multipart/mixed",
                "headers": [
                  {"name":"Subject","value":"Tax question"},
                  {"name":"From","value":"customer@example.com"}
                ],
                "parts": [
                  {
                    "mimeType": "multipart/alternative",
                    "parts": [
                      {"mimeType":"text/plain","body":{"data":"SGVsbG8="}},
                      {"mimeType":"text/html","body":{"data":"SGkh"}}
                    ]
                  },
                  {"filename":"doc.pdf","mimeType":"application/pdf","body":{"attachmentId":"att1","size":2048}}
                ]
              }
            }
            """
        );

        var body = await CreateClient(handler).GetMessageBodyAsync(Guid.NewGuid(), "msg1");

        Assert.Equal(4096, body.MimeSizeBytes);
        Assert.Equal("Hi!", body.HtmlBody);
        Assert.Equal("Hello", body.TextBody);
        Assert.Equal("Tax question", body.Headers["Subject"]);
        Assert.Single(body.Attachments);
        Assert.Equal("att1", body.Attachments[0].ProviderAttachmentId);
        Assert.Contains("format=full", handler.Requests[0].RequestUri!.ToString());
    }

    private static OutboundMessage NewOutboundMessage(string? replyToProviderMessageId = null) =>
        new("Subject", "<p>Html</p>", "Text", ["to@example.com"], [], [], null, null, null, replyToProviderMessageId);

    [Fact]
    public async Task SendMessageAsync_NewMessage_PostsRawMimeWithoutThreadId()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "id": "sent1", "threadId": "thread1" }"""),
            };
        });

        var result = await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), "office@gmail.com", "Office", NewOutboundMessage());

        Assert.Equal("sent1", result.ProviderMessageId);
        Assert.Equal("thread1", result.ProviderThreadId);
        Assert.Contains("/messages/send", handler.Requests[0].RequestUri!.ToString());

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.True(payload.RootElement.TryGetProperty("raw", out _));
        Assert.False(payload.RootElement.TryGetProperty("threadId", out _));
    }

    [Fact]
    public async Task SendMessageAsync_Reply_ResolvesThreadIdFromOriginalMessageAndIncludesItInRequest()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "id": "original1", "threadId": "thread99", "payload": { "headers": [] } }"""
        );
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "id": "sent1", "threadId": "thread99" }"""),
            };
        });

        var result = await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), "office@gmail.com", null, NewOutboundMessage("original1"));

        Assert.Equal("thread99", result.ProviderThreadId);
        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("thread99", payload.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_On403DomainPolicy_ThrowsPermissionDenied()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, """{ "error": { "errors": [ { "reason": "domainPolicy" } ] } }""");

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@gmail.com", null, NewOutboundMessage())
        );

        Assert.Equal(SendFailureReason.PermissionDenied, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_On403DailyLimitExceeded_ThrowsQuotaExceeded()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.Forbidden,
            """{ "error": { "errors": [ { "reason": "dailyLimitExceeded" } ] } }"""
        );

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@gmail.com", null, NewOutboundMessage())
        );

        Assert.Equal(SendFailureReason.QuotaExceeded, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_On401_ThrowsAuthExpired()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@gmail.com", null, NewOutboundMessage())
        );

        Assert.Equal(SendFailureReason.AuthExpired, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_WithAttachment_EmbedsItInTheMimeMultipart()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "id": "sent1", "threadId": "thread1" }"""),
            };
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
            [new OutboundAttachment("doc.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.4 fake"))]
        );

        await CreateClient(handler).SendMessageAsync(Guid.NewGuid(), "office@gmail.com", "Office", message);

        using var payload = JsonDocument.Parse(capturedBody!);
        var raw = payload.RootElement.GetProperty("raw").GetString()!;
        var base64 = raw.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        var mime = MimeMessage.Load(new MemoryStream(Convert.FromBase64String(base64)));

        var attachment = Assert.Single(mime.Attachments);
        var part = Assert.IsType<MimePart>(attachment);
        Assert.Equal("doc.pdf", part.FileName);
        Assert.Equal("application/pdf", part.ContentType.MimeType);
    }
}
