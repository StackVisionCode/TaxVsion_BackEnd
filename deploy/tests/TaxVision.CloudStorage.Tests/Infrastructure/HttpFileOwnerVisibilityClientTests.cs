using System.Net;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Contracts.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Infrastructure.Library;

namespace TaxVision.CloudStorage.Tests.Infrastructure;

public sealed class HttpFileOwnerVisibilityClientTests
{
    private const string Token = "user-token-value";
    private readonly RecordingHandler _handler = new();

    [Fact]
    public async Task It_forwards_the_incoming_bearer_to_the_owner_route()
    {
        var fileId = Guid.NewGuid();
        _handler.Respond = _ => Json(HttpStatusCode.OK, new FileVisibilityResponse([]));

        await Client().CheckAsync(FileOwnerService.Tasks, [fileId], FileVisibilityPurposes.Download, default);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://tasks.local/tasks/files/visibility", request.Uri);
        Assert.Equal($"Bearer {Token}", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(fileId, body.RootElement.GetProperty("fileIds")[0].GetGuid());
        Assert.Equal("download", body.RootElement.GetProperty("purpose").GetString());
    }

    [Fact]
    public async Task Correspondence_is_asked_on_its_own_route()
    {
        _handler.Respond = _ => Json(HttpStatusCode.OK, new FileVisibilityResponse([]));

        await Client()
            .CheckAsync(FileOwnerService.Correspondence, [Guid.NewGuid()], FileVisibilityPurposes.List, default);

        Assert.Equal("http://tasks.local/correspondence/files/visibility", Assert.Single(_handler.Requests).Uri);
    }

    [Fact]
    public async Task A_200_is_available_with_what_the_owner_returned()
    {
        var asked = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var resource = Guid.NewGuid();
        _handler.Respond = _ =>
            Json(
                HttpStatusCode.OK,
                new FileVisibilityResponse([
                    new VisibleFile(
                        asked,
                        "email_thread",
                        resource,
                        "Subject",
                        null,
                        new Dictionary<string, string> { ["messageId"] = "m" }
                    ),
                    new VisibleFile(foreign, "task", Guid.NewGuid(), "Not asked", null),
                ])
            );

        var answer = await Client().CheckAsync(FileOwnerService.Correspondence, [asked], "list", default);

        Assert.True(answer.Available);
        var visible = Assert.Single(answer.Visible).Value;
        Assert.Equal(asked, visible.FileId);
        Assert.Equal(resource, visible.ResourceId);
        Assert.Equal("Subject", visible.Title);
        Assert.Equal("m", visible.Context!["messageId"]);
    }

    [Fact]
    public async Task A_403_is_available_and_empty()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var answer = await Client().CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.True(answer.Available);
        Assert.Empty(answer.Visible);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Other_answers_are_unavailable(HttpStatusCode status)
    {
        _handler.Respond = _ => new HttpResponseMessage(status);

        var answer = await Client().CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.False(answer.Available);
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("http://elsewhere/") },
        };

        var answer = await Client().CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.False(answer.Available);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_timeout_is_unavailable()
    {
        _handler.Delay = TimeSpan.FromSeconds(5);
        _handler.Respond = _ => Json(HttpStatusCode.OK, new FileVisibilityResponse([]));

        var answer = await Client(timeout: TimeSpan.FromMilliseconds(100))
            .CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.False(answer.Available);
    }

    [Fact]
    public async Task An_unreadable_body_is_unavailable()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json"),
        };

        var answer = await Client().CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.False(answer.Available);
    }

    [Fact]
    public async Task Without_a_configured_url_it_is_unavailable_and_calls_nobody()
    {
        var answer = await Client(baseUrl: null).CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.False(answer.Available);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Without_an_incoming_token_it_is_unavailable_and_calls_nobody()
    {
        var answer = await Client(authorization: null)
            .CheckAsync(FileOwnerService.Tasks, [Guid.NewGuid()], "list", default);

        Assert.False(answer.Available);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task More_than_a_batch_is_split_in_calls_of_at_most_a_hundred()
    {
        _handler.Respond = _ => Json(HttpStatusCode.OK, new FileVisibilityResponse([]));
        var ids = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList();

        var answer = await Client().CheckAsync(FileOwnerService.Tasks, ids, "list", default);

        Assert.True(answer.Available);
        Assert.Equal(
            [100, 50],
            _handler.Requests.Select(r =>
                JsonDocument.Parse(r.Body).RootElement.GetProperty("fileIds").GetArrayLength()
            )
        );
    }

    [Fact]
    public async Task No_ids_means_no_call()
    {
        var answer = await Client().CheckAsync(FileOwnerService.Tasks, [], "list", default);

        Assert.True(answer.Available);
        Assert.Empty(_handler.Requests);
    }

    private HttpFileOwnerVisibilityClient Client(
        string? baseUrl = "http://tasks.local/",
        string? authorization = "Bearer " + Token,
        TimeSpan? timeout = null
    )
    {
        var http = new HttpClient(_handler) { Timeout = timeout ?? TimeSpan.FromSeconds(3) };
        if (baseUrl is not null)
            http.BaseAddress = new Uri(baseUrl);
        var context = new DefaultHttpContext();
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;
        return new HttpFileOwnerVisibilityClient(
            new SingleClientFactory(http),
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new LibraryOwnerOptions()),
            NullLogger<HttpFileOwnerVisibilityClient>.Instance
        );
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8,
                "application/json"
            ),
        };

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new SentRequest(
                    request.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.ToString(),
                    body
                )
            );
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            return Respond(request);
        }
    }
}
