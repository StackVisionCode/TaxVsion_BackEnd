using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

namespace TaxVision.Postmaster.Tests.Feedback;

public sealed class Smtp2GoActivityFeedbackPollerTests
{
    private static readonly DateTime SentAt = new(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        public void Enqueue(HttpStatusCode status, string json) =>
            _responses.Enqueue(
                new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
            );

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private static Smtp2GoActivityFeedbackPoller CreatePoller(FakeHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.smtp2go.com/v3/") },
            Options.Create(new Smtp2GoFeedbackOptions { ApiKey = "api-test-key", DelayBetweenRequestsMs = 0 }),
            NullLogger<Smtp2GoActivityFeedbackPoller>.Instance
        );

    private static PendingDeliveryRef Pending(string emailId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", emailId, SentAt);

    private static string Events(params string[] events) =>
        $$$"""{"request_id":"r1","data":{"total_events":{{{events.Length}}},"events":[{{{string.Join(",", events)}}}]}}""";

    private static string ActivityEvent(string emailId, string eventName) =>
        $$"""{"email_id":"{{emailId}}","event":"{{eventName}}","recipient":"customer@example.com","date":"2026-10-21T06:01:00Z","smtp_response":"250 OK"}""";

    [Fact]
    public async Task PollAsync_searches_each_pending_email_with_the_api_key()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, Events(ActivityEvent("email-1", "delivered")));

        var items = await CreatePoller(handler).PollAsync([Pending("email-1")], CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.smtp2go.com/v3/activity/search", request.RequestUri!.ToString());
        Assert.Equal("api-test-key", request.Headers.GetValues("X-Smtp2go-Api-Key").Single());
        Assert.Contains("\"search\":\"email-1\"", handler.Bodies[0]);
        var item = Assert.Single(items);
        Assert.Equal(DeliveryOutcome.Delivered, item.Feedback.Outcome);
        Assert.Equal("email-1", item.ProviderMessageId);
    }

    [Fact]
    public async Task PollAsync_keeps_only_outcomes_of_that_exact_email()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            Events(
                ActivityEvent("email-1", "hard-bounced"),
                // La búsqueda es por texto: puede traer otros correos.
                ActivityEvent("email-12", "delivered"),
                // Las aperturas llegan por el webhook; contarlas acá las duplicaría.
                ActivityEvent("email-1", "opened")
            )
        );

        var items = await CreatePoller(handler).PollAsync([Pending("email-1")], CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(DeliveryOutcome.HardBounced, item.Feedback.Outcome);
    }

    [Fact]
    public async Task PollAsync_stops_when_the_api_fails()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}");

        var items = await CreatePoller(handler)
            .PollAsync([Pending("email-1"), Pending("email-2")], CancellationToken.None);

        Assert.Empty(items);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PollAsync_skips_recipients_without_provider_id()
    {
        var handler = new FakeHttpMessageHandler();

        var items = await CreatePoller(handler)
            .PollAsync(
                [new PendingDeliveryRef(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", null, SentAt)],
                CancellationToken.None
            );

        Assert.Empty(items);
        Assert.Empty(handler.Requests);
    }
}
