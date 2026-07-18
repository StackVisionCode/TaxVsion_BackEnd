using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Infrastructure.Customers;

namespace TaxVision.Correspondence.Tests.Infrastructure;

/// <summary>
/// Fase 1 (hardening) — antes de este fix, <c>FetchPageAsync</c> filtraba
/// <c>ex is not OperationCanceledException</c>, que deja pasar sin atrapar (no matchea el catch)
/// exactamente el <see cref="TaskCanceledException"/> que dispara el timeout de 30s del
/// <c>HttpClient</c> (ver DependencyInjection) — subía sin atrapar en vez de resolver a <c>null</c>
/// como documenta <c>ICorrespondenceCustomerClient.ListActiveCustomersAsync</c> ("el caller decide
/// cómo reintentar/loguear, nunca lanza"). Habilitado por <c>InternalsVisibleTo</c> en
/// Infrastructure/AssemblyInfo.cs, mismo objetivo que CloudStorageOutboundAttachmentFetcherTests
/// (Postmaster, Fase 13) pero contra un tipo internal.
/// </summary>
public sealed class CorrespondenceCustomerClientTests
{
    private sealed class FaultingHandler(Func<Exception> exceptionFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw exceptionFactory();
    }

    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
            );
    }

    private sealed class FakeTokenAcquirer(string? token = "fake-token") : ICorrespondenceServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    [Fact]
    public async Task ListActiveCustomersAsync_WhenHttpCallThrowsHttpRequestException_ReturnsNullInsteadOfThrowing()
    {
        var httpClient = new HttpClient(new FaultingHandler(() => new HttpRequestException("Connection refused.")))
        {
            BaseAddress = new Uri("http://localhost:5210/"),
        };
        var client = new CorrespondenceCustomerClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CorrespondenceCustomerClient>.Instance
        );

        var result = await client.ListActiveCustomersAsync(Guid.NewGuid(), 1, 100, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListActiveCustomersAsync_WhenHttpCallThrowsTaskCanceledException_ReturnsNullInsteadOfThrowing()
    {
        // TaskCanceledException cubre tanto cancelación explícita como el timeout de 30s del
        // HttpClient; antes del fix este caso puntual subía sin atrapar (ver comentario de clase).
        var httpClient = new HttpClient(new FaultingHandler(() => new TaskCanceledException("The request timed out.")))
        {
            BaseAddress = new Uri("http://localhost:5210/"),
        };
        var client = new CorrespondenceCustomerClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CorrespondenceCustomerClient>.Instance
        );

        var result = await client.ListActiveCustomersAsync(Guid.NewGuid(), 1, 100, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListActiveCustomersAsync_WithoutServiceToken_ReturnsNullWithoutCallingHttp()
    {
        var httpClient = new HttpClient(
            new FaultingHandler(() => new InvalidOperationException("Should not be called."))
        )
        {
            BaseAddress = new Uri("http://localhost:5210/"),
        };
        var client = new CorrespondenceCustomerClient(
            httpClient,
            new FakeTokenAcquirer(token: null),
            NullLogger<CorrespondenceCustomerClient>.Instance
        );

        var result = await client.ListActiveCustomersAsync(Guid.NewGuid(), 1, 100, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListActiveCustomersAsync_WithAValidPage_ReturnsMappedResult()
    {
        var handler = new QueuedHandler();
        var customerId = Guid.NewGuid();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"items":[{"id":"{{customerId}}","primaryEmail":"a@b.com","status":"Active"}],"page":1,"size":100,"totalCount":1}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var client = new CorrespondenceCustomerClient(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CorrespondenceCustomerClient>.Instance
        );

        var result = await client.ListActiveCustomersAsync(Guid.NewGuid(), 1, 100, CancellationToken.None);

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(customerId, item.Id);
        Assert.True(item.IsActive);
    }
}
