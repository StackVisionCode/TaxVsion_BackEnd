using System.Diagnostics;
using System.Net;
using System.Text;
using BuildingBlocks.Contracts.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Infrastructure.Library;

namespace TaxVision.CloudStorage.Tests.Infrastructure;

/// <summary>Ámbito de Communication y rutas de los dueños nuevos, con un <c>HttpMessageHandler</c> falso.</summary>
public sealed class FileOwnerVisibilityClientScopeTests
{
    private const string Token = "user-token-value";
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, string> _clientNames = [];
    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK);
    private TimeSpan _delay = TimeSpan.Zero;

    [Fact]
    public async Task The_scope_is_asked_with_the_incoming_bearer()
    {
        var conversation = Guid.NewGuid();
        _respond = () =>
            Json($$"""{"conversationIds":["{{conversation}}"],"callIds":[],"meetingIds":[],"truncated":["calls"]}""");

        var answer = await Client().GetCommunicationScopeAsync(CancellationToken.None);

        Assert.True(answer.Available);
        Assert.Equal([conversation], answer.Scope.ConversationIds);
        Assert.Equal(["calls"], answer.Scope.Truncated);
        var request = Assert.Single(_requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://owner.local/communication/files/scope", request.RequestUri!.ToString());
        Assert.Equal($"Bearer {Token}", request.Headers.Authorization!.ToString());
        Assert.Equal(HttpFileOwnerVisibilityClient.CommunicationClient, _clientNames["last"]);
    }

    [Fact]
    public async Task The_scope_gives_up_after_its_own_two_seconds()
    {
        _delay = TimeSpan.FromSeconds(10);
        var watch = Stopwatch.StartNew();

        var answer = await Client(clientTimeout: TimeSpan.FromSeconds(30)).GetCommunicationScopeAsync(default);

        Assert.False(answer.Available);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task Without_a_configured_url_the_scope_is_unavailable_and_nothing_is_called()
    {
        var answer = await Client(baseUrl: null).GetCommunicationScopeAsync(default);

        Assert.False(answer.Available);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Without_an_incoming_token_the_scope_is_unavailable()
    {
        var answer = await Client(authorization: null).GetCommunicationScopeAsync(default);

        Assert.False(answer.Available);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task A_403_scope_is_available_and_empty()
    {
        _respond = () => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var answer = await Client().GetCommunicationScopeAsync(default);

        Assert.True(answer.Available);
        Assert.Empty(answer.Scope.ConversationIds);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Other_scope_answers_are_unavailable(HttpStatusCode status)
    {
        _respond = () => new HttpResponseMessage(status);

        Assert.False((await Client().GetCommunicationScopeAsync(default)).Available);
    }

    [Theory]
    [InlineData(FileOwnerService.Communication, "communication/files/visibility", "library-owner-communication")]
    [InlineData(FileOwnerService.Notes, "notes/files/visibility", "library-owner-notes")]
    public async Task The_new_owners_are_asked_on_their_own_routes(FileOwnerService owner, string path, string client)
    {
        _respond = () => Json("""{"items":[]}""");

        var answer = await Client().CheckAsync(owner, [Guid.NewGuid()], FileVisibilityPurposes.List, default);

        Assert.True(answer.Available);
        Assert.Equal($"http://owner.local/{path}", Assert.Single(_requests).RequestUri!.ToString());
        Assert.Equal(client, _clientNames["last"]);
    }

    private HttpFileOwnerVisibilityClient Client(
        string? baseUrl = "http://owner.local/",
        string? authorization = "Bearer " + Token,
        TimeSpan? clientTimeout = null
    )
    {
        var http = new HttpClient(new Handler(this)) { Timeout = clientTimeout ?? TimeSpan.FromSeconds(3) };
        if (baseUrl is not null)
            http.BaseAddress = new Uri(baseUrl);
        var context = new DefaultHttpContext();
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;
        return new HttpFileOwnerVisibilityClient(
            new NamedClientFactory(http, _clientNames),
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new LibraryOwnerOptions()),
            NullLogger<HttpFileOwnerVisibilityClient>.Instance
        );
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class NamedClientFactory(HttpClient client, Dictionary<string, string> names) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            names["last"] = name;
            return client;
        }
    }

    private sealed class Handler(FileOwnerVisibilityClientScopeTests test) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            test._requests.Add(request);
            if (test._delay > TimeSpan.Zero)
                await Task.Delay(test._delay, cancellationToken);
            return test._respond();
        }
    }
}
