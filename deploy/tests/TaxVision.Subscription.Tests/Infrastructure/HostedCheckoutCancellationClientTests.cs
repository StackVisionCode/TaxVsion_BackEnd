using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;
using TaxVision.Subscription.Infrastructure.PaymentApp;

namespace TaxVision.Subscription.Tests.Infrastructure;

/// <summary>Fail-open hacia la red interna: 200/404/5xx/timeout todos devuelven Success. El intent local ya
/// está Cancelled, así que un fallo del provider nunca bloquea al usuario.</summary>
public sealed class HostedCheckoutCancellationClientTests
{
    [Fact]
    public async Task Returns_success_on_200()
    {
        var client = BuildClient(HandlerThatReturns(HttpStatusCode.OK, @"{""alreadyExpired"":false}"));

        var result = await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Returns_success_on_404_without_erroring()
    {
        var client = BuildClient(HandlerThatReturns(HttpStatusCode.NotFound, string.Empty));

        var result = await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Returns_success_on_500_without_erroring()
    {
        var client = BuildClient(HandlerThatReturns(HttpStatusCode.InternalServerError, string.Empty));

        var result = await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Returns_success_when_the_http_call_throws()
    {
        var client = BuildClient(HandlerThatThrows(new HttpRequestException("boom")));

        var result = await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Returns_success_when_the_http_call_times_out()
    {
        var client = BuildClient(HandlerThatThrows(new TaskCanceledException("timeout")));

        var result = await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Returns_success_when_no_service_token_is_available()
    {
        var client = new HostedCheckoutCancellationClient(
            new HttpClient { BaseAddress = new Uri("http://localhost/") },
            new FakeTokenAcquirer(token: null),
            NullLogger<HostedCheckoutCancellationClient>.Instance
        );

        var result = await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static HostedCheckoutCancellationClient BuildClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            new FakeTokenAcquirer(token: "fake-token"),
            NullLogger<HostedCheckoutCancellationClient>.Instance
        );

    private static HttpMessageHandler HandlerThatReturns(HttpStatusCode status, string body) =>
        new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    private static HttpMessageHandler HandlerThatThrows(Exception ex) => new StubHandler(_ => throw ex);

    private sealed class FakeTokenAcquirer(string? token) : IGrowthServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
