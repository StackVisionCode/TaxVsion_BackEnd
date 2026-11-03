using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;
using static TaxVision.Postmaster.Tests.Feedback.FakeSmtp2GoHttpHandler;

namespace TaxVision.Postmaster.Tests.Feedback;

public sealed class Smtp2GoActivityFeedbackPollerTests
{
    private static readonly DateTime SentAt = new(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowFrom = new(2026, 10, 19, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    internal static Smtp2GoActivityFeedbackPoller CreatePoller(FakeSmtp2GoHttpHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.smtp2go.com/v3/") },
            Options.Create(new Smtp2GoFeedbackOptions { ApiKey = "api-test-key", DelayBetweenRequestsMs = 0 }),
            NullLogger<Smtp2GoActivityFeedbackPoller>.Instance
        );

    private static PendingDeliveryRef Pending(string emailId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", emailId, SentAt);

    private static string Events(params string[] events) => Page(null, events);

    [Fact]
    public async Task PollAsync_searches_each_pending_email_with_the_api_key()
    {
        var handler = new FakeSmtp2GoHttpHandler();
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
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            Events(
                ActivityEvent("email-1", "hard-bounced"),
                // La búsqueda es por texto: puede traer otros correos.
                ActivityEvent("email-12", "delivered"),
                // Las aperturas tienen su propia consulta por rango.
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
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}");

        var items = await CreatePoller(handler)
            .PollAsync([Pending("email-1"), Pending("email-2")], CancellationToken.None);

        Assert.Empty(items);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PollAsync_skips_recipients_without_provider_id()
    {
        var handler = new FakeSmtp2GoHttpHandler();

        var items = await CreatePoller(handler)
            .PollAsync(
                [new PendingDeliveryRef(Guid.NewGuid(), Guid.NewGuid(), "a@b.com", null, SentAt)],
                CancellationToken.None
            );

        Assert.Empty(items);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PollEngagementAsync_asks_by_date_range_and_event_types()
    {
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, Page(null, ActivityEvent("email-1", "opened")));

        await CreatePoller(handler).PollEngagementAsync(WindowFrom, WindowTo, 2000, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.smtp2go.com/v3/activity/search", request.RequestUri!.ToString());
        Assert.Equal("api-test-key", request.Headers.GetValues("X-Smtp2go-Api-Key").Single());
        var body = JsonNode.Parse(handler.Bodies[0])!.AsObject();
        Assert.Equal("2026-10-19T18:00:00Z", (string?)body["start_date"]);
        Assert.Equal("2026-10-21T18:00:00Z", (string?)body["end_date"]);
        Assert.Equal(["delivered", "opened", "clicked"], body["event_types"]!.AsArray().Select(n => (string?)n));
        // El máximo por página de la API, aunque el tope por corrida sea mayor.
        Assert.Equal(1000, (int?)body["limit"]);
        Assert.False(body.ContainsKey("search"));
        Assert.False(body.ContainsKey("continue_token"));
    }

    [Fact]
    public async Task PollEngagementAsync_translates_deliveries_opens_and_clicks()
    {
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            Page(
                null,
                ActivityEvent("email-1", "opened"),
                ActivityEvent("email-2", "clicked", recipient: "other@example.com"),
                ActivityEvent("email-3", "delivered"),
                // Otros eventos, o lo que no se puede atar a un correo, se descartan.
                ActivityEvent("email-5", "processed"),
                """{"event":"opened","recipient":"customer@example.com","date":"2026-10-21T06:01:00Z"}""",
                """{"email_id":"email-4","event":"clicked","date":"2026-10-21T06:01:00Z"}"""
            )
        );

        var items = await CreatePoller(handler).PollEngagementAsync(WindowFrom, WindowTo, 2000, CancellationToken.None);

        Assert.Equal(3, items.Count);
        Assert.Equal(DeliveryOutcome.Delivered, items[2].Feedback.Outcome);
        Assert.Equal("email-3", items[2].ProviderMessageId);
        Assert.Equal(EngagementSignal.Opened, items[0].Feedback.Signal);
        Assert.Equal("email-1", items[0].ProviderMessageId);
        Assert.Equal("customer@example.com", items[0].RecipientAddress);
        Assert.Equal(new DateTime(2026, 10, 21, 6, 1, 0, DateTimeKind.Utc), items[0].Feedback.OccurredAtUtc);
        Assert.Equal(EngagementSignal.Clicked, items[1].Feedback.Signal);
        Assert.Equal("other@example.com", items[1].RecipientAddress);
    }

    [Fact]
    public async Task PollEngagementAsync_follows_the_continue_token_until_the_last_page()
    {
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, Page("page-2", ActivityEvent("email-1", "opened")));
        handler.Enqueue(HttpStatusCode.OK, Page(null, ActivityEvent("email-2", "clicked")));

        var items = await CreatePoller(handler).PollEngagementAsync(WindowFrom, WindowTo, 2000, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var second = JsonNode.Parse(handler.Bodies[1])!.AsObject();
        Assert.Equal("page-2", (string?)second["continue_token"]);
        // La página siguiente repite los mismos filtros.
        Assert.Equal("2026-10-19T18:00:00Z", (string?)second["start_date"]);
        Assert.Equal(["email-1", "email-2"], items.Select(i => i.ProviderMessageId));
    }

    [Fact]
    public async Task PollEngagementAsync_stops_at_the_cap_per_run()
    {
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            Page("page-2", ActivityEvent("email-1", "opened"), ActivityEvent("email-2", "opened"))
        );
        handler.Enqueue(
            HttpStatusCode.OK,
            Page("page-3", ActivityEvent("email-3", "opened"), ActivityEvent("email-4", "opened"))
        );

        var items = await CreatePoller(handler).PollEngagementAsync(WindowFrom, WindowTo, 3, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, (int?)JsonNode.Parse(handler.Bodies[1])!["limit"]);
        Assert.Equal(["email-1", "email-2", "email-3"], items.Select(i => i.ProviderMessageId));
    }

    [Fact]
    public async Task PollEngagementAsync_keeps_what_it_read_when_the_api_fails()
    {
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, Page("page-2", ActivityEvent("email-1", "opened")));
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}");

        var items = await CreatePoller(handler).PollEngagementAsync(WindowFrom, WindowTo, 2000, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("email-1", Assert.Single(items).ProviderMessageId);
    }

    [Fact]
    public async Task PollEngagementAsync_stops_on_an_empty_page_even_with_a_token()
    {
        var handler = new FakeSmtp2GoHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, Page("page-2"));

        var items = await CreatePoller(handler).PollEngagementAsync(WindowFrom, WindowTo, 2000, CancellationToken.None);

        Assert.Empty(items);
        Assert.Single(handler.Requests);
    }
}
