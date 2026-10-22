using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;
using TaxVision.Postmaster.Infrastructure.Providers.Connectors;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class ConnectorsSendClientTests
{
    /// <summary>Encola respuestas HTTP en orden — mismo patrón que <c>FakeHttpMessageHandler</c> de Connectors.Tests.</summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<string> RequestBodies { get; } = [];

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private sealed class FakeTokenAcquirer(string? token = "fake-token") : IPostmasterServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private static SentMessage CreateMessage()
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "sales@tenant.example",
                EmailStream.Transactional,
                "gmail",
                Guid.NewGuid(),
                "corr-1",
                "Tenant Sales",
                replyTo: null,
                "auth.welcome",
                DateTime.UtcNow,
                ProviderScope.TenantMailbox
            )
            .Value;
        message.AddRecipient("customer@example.com", RecipientType.To, null);
        return message;
    }

    private static ResolvedMailbox CreateProvider(Guid accountId) =>
        new(accountId, "gmail", "sales@tenant.example", "Tenant Sales");

    [Fact]
    public async Task SendAsync_sends_our_message_id_and_returns_the_one_the_mailbox_used()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"providerMessageId":"draft-1","providerThreadId":"conv-1","sentAtUtc":"2026-07-17T00:00:00Z","internetMessageId":"<AM6P@outlook.com>"}"""
                ),
            }
        );
        var client = new ConnectorsSendClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") },
            new FakeTokenAcquirer(),
            NullLogger<ConnectorsSendClient>.Instance
        );
        var message = CreateMessage();

        var result = await client.SendAsync(
            message,
            new RenderedContent("Welcome", "<p>Hi</p>", "Hi"),
            CreateProvider(Guid.NewGuid()),
            null,
            null,
            null,
            attachments: [],
            inlineAssets: [],
            CancellationToken.None
        );

        // Con nuestro id el aviso de no entrega se ata solo; si el buzón lo cambia, vale el que devuelve.
        Assert.Contains($"pm.{message.Id:N}@", handler.RequestBodies[0]);
        Assert.Equal("<AM6P@outlook.com>", result.InternetMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "SendMessageHandler.AuthExpired", MailboxSendFailure.MailboxUnavailable)]
    [InlineData(HttpStatusCode.Forbidden, "SendMessageHandler.PermissionDenied", MailboxSendFailure.MailboxUnavailable)]
    [InlineData(HttpStatusCode.NotFound, "TenantEmailAccount.NotFound", MailboxSendFailure.MailboxUnavailable)]
    [InlineData(
        HttpStatusCode.BadGateway,
        "SendMessageHandler.TransientProviderError",
        MailboxSendFailure.ProviderUnavailable
    )]
    [InlineData(HttpStatusCode.GatewayTimeout, "SendMessageHandler.Timeout", MailboxSendFailure.ProviderUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, "SendMessageHandler.InvalidRequest", MailboxSendFailure.InvalidMessage)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Something.Else", MailboxSendFailure.ProviderUnavailable)]
    public async Task SendAsync_translates_the_failure_to_whose_fault_it_was(
        HttpStatusCode status,
        string code,
        MailboxSendFailure expected
    )
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            new HttpResponseMessage(status)
            {
                Content = new StringContent($$"""{"code":"{{code}}","message":"failed"}"""),
            }
        );
        var client = new ConnectorsSendClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") },
            new FakeTokenAcquirer(),
            NullLogger<ConnectorsSendClient>.Instance
        );

        var result = await client.SendAsync(
            CreateMessage(),
            new RenderedContent("Welcome", "<p>Hi</p>", "Hi"),
            CreateProvider(Guid.NewGuid()),
            null,
            null,
            null,
            attachments: [],
            inlineAssets: [],
            CancellationToken.None
        );

        Assert.False(result.Success);
        Assert.Equal(expected, result.MailboxFailure);
    }

    [Fact]
    public async Task SendAsync_returns_success_with_ProviderMessageId_on_200()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"providerMessageId":"gmail-msg-1","providerThreadId":"thread-1","sentAtUtc":"2026-07-17T00:00:00Z"}"""
                ),
            }
        );
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") };
        var client = new ConnectorsSendClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<ConnectorsSendClient>.Instance
        );
        var message = CreateMessage();
        var content = new RenderedContent("Welcome", "<p>Hi</p>", "Hi");
        var accountId = Guid.NewGuid();

        var result = await client.SendAsync(
            message,
            content,
            CreateProvider(accountId),
            null,
            null,
            null,
            attachments: [],
            inlineAssets: [],
            CancellationToken.None
        );

        Assert.True(result.Success);
        Assert.Equal("gmail-msg-1", result.ProviderMessageId);
        // Antes se perdía: Correspondence lo necesita para seguir el hilo.
        Assert.Equal("thread-1", result.ProviderThreadId);
        Assert.All(result.RecipientOutcomes, o => Assert.Equal(RecipientSendStatus.Sent, o.Status));
    }

    [Fact]
    public async Task SendAsync_returns_failure_with_reason_from_error_body_on_non_success_status()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    """{"code":"SendMessageHandler.QuotaExceeded","message":"Daily send limit reached."}"""
                ),
            }
        );
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") };
        var client = new ConnectorsSendClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<ConnectorsSendClient>.Instance
        );
        var message = CreateMessage();
        var content = new RenderedContent("Welcome", "<p>Hi</p>", "Hi");

        var result = await client.SendAsync(
            message,
            content,
            CreateProvider(Guid.NewGuid()),
            null,
            null,
            null,
            attachments: [],
            inlineAssets: [],
            CancellationToken.None
        );

        Assert.False(result.Success);
        Assert.Contains("QuotaExceeded", result.ErrorReason);
        Assert.All(result.RecipientOutcomes, o => Assert.Equal(RecipientSendStatus.Rejected, o.Status));
        Assert.Null(result.RetryAfter); // el cupo diario no se arregla reintentando en minutos
    }

    [Fact]
    public async Task SendAsync_marks_the_per_minute_throttle_as_retryable_with_its_wait()
    {
        var handler = new FakeHttpMessageHandler();
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"code":"SendMessageHandler.RateLimited","message":"Too many sends."}"""),
        };
        throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(25));
        handler.Enqueue(throttled);
        var client = new ConnectorsSendClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") },
            new FakeTokenAcquirer(),
            NullLogger<ConnectorsSendClient>.Instance
        );

        var result = await client.SendAsync(
            CreateMessage(),
            new RenderedContent("Welcome", "<p>Hi</p>", "Hi"),
            CreateProvider(Guid.NewGuid()),
            null,
            null,
            null,
            attachments: [],
            inlineAssets: [],
            CancellationToken.None
        );

        Assert.False(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(25), result.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_returns_failure_without_calling_http_when_no_token_available()
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") };
        var client = new ConnectorsSendClient(
            httpClient,
            new FakeTokenAcquirer(token: null),
            NullLogger<ConnectorsSendClient>.Instance
        );
        var message = CreateMessage();
        var content = new RenderedContent("Welcome", "<p>Hi</p>", "Hi");

        var result = await client.SendAsync(
            message,
            content,
            CreateProvider(Guid.NewGuid()),
            null,
            null,
            null,
            attachments: [],
            inlineAssets: [],
            CancellationToken.None
        );

        Assert.False(result.Success);
        Assert.Contains("credentials", result.ErrorReason);
    }

    [Fact]
    public async Task SendAsync_WithAttachments_IncludesBase64EncodedContentInTheRequestBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"providerMessageId":"gmail-msg-1","providerThreadId":null,"sentAtUtc":"2026-07-17T00:00:00Z"}"""
                ),
            }
        );
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5390/") };
        var client = new ConnectorsSendClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<ConnectorsSendClient>.Instance
        );
        var message = CreateMessage();
        var content = new RenderedContent("Welcome", "<p>Hi</p>", "Hi");
        var attachmentBytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake");

        var result = await client.SendAsync(
            message,
            content,
            CreateProvider(Guid.NewGuid()),
            null,
            null,
            null,
            attachments: [new OutboundAttachmentBytes("invoice.pdf", "application/pdf", attachmentBytes)],
            inlineAssets: [],
            CancellationToken.None
        );

        Assert.True(result.Success);
        var requestBody = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"invoice.pdf\"", requestBody);
        Assert.Contains(Convert.ToBase64String(attachmentBytes), requestBody);
    }
}
