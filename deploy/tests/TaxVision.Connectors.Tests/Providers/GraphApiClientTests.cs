using System.Net;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Resilience;
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
            new HttpResiliencePipelineRegistry(),
            NullLogger<GraphApiClient>.Instance
        );

    [Fact]
    public async Task GetHistoryAsync_FirstSync_StartsFromTheConnectionDateInsteadOfTheWholeInbox()
    {
        var handler = new FakeHttpMessageHandler();
        string? requestedUrl = null;
        handler.Enqueue(request =>
        {
            requestedUrl = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=x" }"""
                ),
            };
        });

        await CreateClient(handler)
            .GetHistoryAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                new DateTime(2026, 11, 3, 2, 18, 5, DateTimeKind.Unspecified)
            );

        Assert.Contains("$filter=receivedDateTime+ge+2026-11-03T02:18:05Z", Uri.UnescapeDataString(requestedUrl!));
    }

    [Fact]
    public async Task GetHistoryAsync_WithCursor_IgnoresTheFirstSyncDate()
    {
        var handler = new FakeHttpMessageHandler();
        string? requestedUrl = null;
        handler.Enqueue(request =>
        {
            requestedUrl = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "value": [], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=y" }"""
                ),
            };
        });

        await CreateClient(handler)
            .GetHistoryAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://graph.microsoft.com/v1.0/delta-old",
                DateTime.UtcNow
            );

        Assert.DoesNotContain("filter", requestedUrl!);
    }

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

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), null);

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

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.Equal(["msg1", "msg2"], result.NewMessageIds);
        Assert.Equal("https://graph.microsoft.com/v1.0/delta-final", result.NextCursor);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_On410_RestartsDeltaFromScratch()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Gone, """{ "error": { "code": "SyncStateNotFound" } }""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ { "id": "msg1" } ], "@odata.deltaLink": "https://graph.microsoft.com/v1.0/delta-new" }"""
        );

        var result = await CreateClient(handler)
            .GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), "https://graph.microsoft.com/v1.0/delta-expired");

        Assert.Equal(["msg1"], result.NewMessageIds);
        Assert.Equal("https://graph.microsoft.com/v1.0/delta-new", result.NextCursor);
        Assert.Contains("/mailFolders/inbox/messages/delta", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetHistoryAsync_WhenPageCapReached_KeepsNextLinkAsCursor()
    {
        var handler = new FakeHttpMessageHandler();
        for (var page = 0; page < 10; page++)
            handler.Enqueue(
                HttpStatusCode.OK,
                $$"""{ "value": [ { "id": "msg-{{page}}" } ], "@odata.nextLink": "https://graph.microsoft.com/v1.0/next-{{page}}" }"""
            );

        var result = await CreateClient(handler)
            .GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), "https://graph.microsoft.com/v1.0/delta-old");

        // Guardar el cursor viejo hacía releer las mismas 10 páginas en cada pasada.
        Assert.True(result.HasMore);
        Assert.Equal("https://graph.microsoft.com/v1.0/next-9", result.NextCursor);
    }

    [Fact]
    public async Task GetHistoryAsync_SkipsRemovedItems()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "value": [ { "id": "msg1" }, { "id": "msg2", "@removed": { "reason": "deleted" } } ],
              "@odata.deltaLink": "https://graph.microsoft.com/v1.0/delta-final"
            }
            """
        );

        var result = await CreateClient(handler).GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.Equal(["msg1"], result.NewMessageIds);
    }

    [Fact]
    public async Task GetRawMessageAsync_ReadsTheMimeFromValue()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("Subject: Undeliverable\r\n\r\nx")),
        });

        var bytes = await CreateClient(handler).GetRawMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "msg-1");

        Assert.StartsWith("Subject: Undeliverable", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.EndsWith("/me/messages/msg-1/$value", handler.Requests[0].RequestUri!.ToString());
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

        var message = await CreateClient(handler).GetMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "msg1");

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

        await using var stream = await CreateClient(handler)
            .GetAttachmentAsync(Guid.NewGuid(), Guid.NewGuid(), "msg1", "att1");
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
        var result = await CreateClient(handler, rateLimiter).GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), null);

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

        var body = await CreateClient(handler).GetMessageBodyAsync(Guid.NewGuid(), Guid.NewGuid(), "msg1");

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

        var body = await CreateClient(handler).GetMessageBodyAsync(Guid.NewGuid(), Guid.NewGuid(), "msg1");

        Assert.Equal("Hi!", body.TextBody);
        Assert.Null(body.HtmlBody);
    }

    private static OutboundMessage NewOutboundMessage(string? replyToProviderMessageId = null) =>
        new("Subject", "<p>Html</p>", "Text", ["to@example.com"], [], [], null, null, null, replyToProviderMessageId);

    [Fact]
    public async Task SendMessageAsync_NewMessage_CreatesADraftThenSendsItAndReturnsItsInternetMessageId()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{ "id": "draft-1", "internetMessageId": "<AM6P@outlook.com>", "conversationId": "conv-1" }"""
                ),
            };
        });
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage());

        // sendMail no devolvía nada; el borrador sí trae el id que van a citar los rebotes.
        Assert.Equal("<AM6P@outlook.com>", result.InternetMessageId);
        Assert.Equal("draft-1", result.ProviderMessageId);
        Assert.Equal("conv-1", result.ProviderThreadId);
        Assert.EndsWith("/me/messages", handler.Requests[0].RequestUri!.ToString());
        Assert.EndsWith("/me/messages/draft-1/send", handler.Requests[1].RequestUri!.ToString());

        using var payload = JsonDocument.Parse(capturedBody!);
        var toAddress = payload
            .RootElement.GetProperty("toRecipients")[0]
            .GetProperty("emailAddress")
            .GetProperty("address")
            .GetString();
        Assert.Equal("to@example.com", toAddress);
    }

    [Fact]
    public async Task SendMessageAsync_Reply_CreatesAReplyDraftThenSendsItAndReturnsItsInternetMessageId()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{ "id": "reply-draft-1", "internetMessageId": "<AM6PREPLY@outlook.com>", "conversationId": "conv-9", "isDraft": true }"""
                ),
            };
        });
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await CreateClient(handler)
            .SendMessageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "office@outlook.com",
                null,
                NewOutboundMessage("original1")
            );

        // El id del borrador es el que van a citar los rebotes de la respuesta.
        Assert.Equal("<AM6PREPLY@outlook.com>", result.InternetMessageId);
        Assert.Equal("reply-draft-1", result.ProviderMessageId);
        Assert.Equal("conv-9", result.ProviderThreadId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/me/messages/original1/createReply", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("outlook.body-content-type=\"html\"", handler.Requests[0].Headers.GetValues("Prefer").Single());
        Assert.EndsWith("/me/messages/reply-draft-1/send", handler.Requests[1].RequestUri!.ToString());

        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<p>Html</p>", payload.RootElement.GetProperty("comment").GetString());
        var toAddress = payload
            .RootElement.GetProperty("message")
            .GetProperty("toRecipients")[0]
            .GetProperty("emailAddress")
            .GetProperty("address")
            .GetString();
        Assert.Equal("to@example.com", toAddress);
    }

    [Fact]
    public async Task SendMessageAsync_Reply_WhenTheDraftCannotBeSent_ThrowsWithTheSendFailure()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.Created,
            """{ "id": "reply-draft-1", "internetMessageId": "<AM6PREPLY@outlook.com>" }"""
        );
        handler.Enqueue(HttpStatusCode.BadRequest, "{}");

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler)
                .SendMessageAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "office@outlook.com",
                    null,
                    NewOutboundMessage("original1")
                )
        );

        Assert.Equal(SendFailureReason.InvalidRequest, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_Reply_WithoutDraftPermission_FallsBackToOneStepReplyWithoutId()
    {
        // Un token con Mail.Send pero sin Mail.ReadWrite no puede crear borradores (403).
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "{}");
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await CreateClient(handler)
            .SendMessageAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "office@outlook.com",
                null,
                NewOutboundMessage("original1")
            );

        Assert.Null(result.InternetMessageId);
        Assert.Null(result.ProviderMessageId);
        Assert.EndsWith("/me/messages/original1/createReply", handler.Requests[0].RequestUri!.ToString());
        Assert.EndsWith("/me/messages/original1/reply", handler.Requests[1].RequestUri!.ToString());
        Assert.Equal("outlook.body-content-type=\"html\"", handler.Requests[1].Headers.GetValues("Prefer").Single());
    }

    [Fact]
    public async Task SendMessageAsync_NewMessage_WithoutDraftPermission_FallsBackToSendMail()
    {
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.Enqueue(HttpStatusCode.Forbidden, "{}");
        handler.Enqueue(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        var result = await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage());

        Assert.Null(result.InternetMessageId);
        Assert.EndsWith("/me/sendMail", handler.Requests[1].RequestUri!.ToString());
        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.True(payload.RootElement.GetProperty("saveToSentItems").GetBoolean());
        Assert.Equal("Subject", payload.RootElement.GetProperty("message").GetProperty("subject").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_Reply_ForbiddenTwice_ThrowsPermissionDenied()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "{}");
        handler.Enqueue(HttpStatusCode.Forbidden, "{}");

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler)
                .SendMessageAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "office@outlook.com",
                    null,
                    NewOutboundMessage("original1")
                )
        );

        Assert.Equal(SendFailureReason.PermissionDenied, exception.Reason);
    }

    [Fact]
    public async Task SendMessageAsync_On401_ThrowsAuthExpired()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        var exception = await Assert.ThrowsAsync<OutboundEmailSendException>(() =>
            CreateClient(handler)
                .SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage())
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
                .SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "office@outlook.com", null, NewOutboundMessage())
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
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{ "id": "draft-1", "internetMessageId": "<x@outlook.com>" }"""),
            };
        });
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
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

        await CreateClient(handler)
            .SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "office@outlook.com", null, message);

        using var payload = JsonDocument.Parse(capturedBody!);
        var attachment = payload.RootElement.GetProperty("attachments")[0];
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
            CreateClient(handler).SendMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "office@outlook.com", null, message)
        );

        Assert.Equal(SendFailureReason.AttachmentTooLarge, exception.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetMessage_404_ThrowsNotFound()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, """{ "error": { "code": 404 } }""");

        var ex = await Assert.ThrowsAsync<EmailProviderException>(() =>
            CreateClient(handler).GetMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "msg1")
        );

        Assert.True(ex.IsNotFound);
    }

    [Fact]
    public async Task GetMessage_503_ThrowsTransient()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "{}");

        var ex = await Assert.ThrowsAsync<EmailProviderException>(() =>
            CreateClient(handler).GetMessageAsync(Guid.NewGuid(), Guid.NewGuid(), "msg1")
        );

        Assert.False(ex.IsNotFound);
    }

    [Fact]
    public async Task ListInboxMessageIds_FiltersInboxByReceivedDateAndReturnsTheNextLink()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{ "value": [ { "id": "m1" }, { "id": "m2" } ], "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skip=50" }"""
        );
        handler.Enqueue(HttpStatusCode.OK, """{ "value": [ { "id": "m3" } ] }""");
        var from = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
        var client = CreateClient(handler);

        var first = await client.ListInboxMessageIdsAsync(Guid.NewGuid(), Guid.NewGuid(), from, to, null);
        var second = await client.ListInboxMessageIdsAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            from,
            to,
            first.NextPageToken
        );

        Assert.Equal(["m1", "m2"], first.MessageIds);
        Assert.Equal(["m3"], second.MessageIds);
        Assert.Null(second.NextPageToken);
        var url = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("/me/mailFolders/inbox/messages?", url);
        Assert.Contains("receivedDateTime ge 2026-11-03T00:00:00Z and receivedDateTime lt 2026-11-04T00:00:00Z", url);
        Assert.Contains("$select=id", url);
        Assert.Equal(first.NextPageToken, handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task ListInboxMessageIds_RejectsAPageTokenOutsideGraph()
    {
        var handler = new FakeHttpMessageHandler();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateClient(handler)
                .ListInboxMessageIdsAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow,
                    "https://evil.example/steal"
                )
        );
        Assert.Empty(handler.Requests);
    }
}
