using System.Net;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Providers;

/// <summary>Encola respuestas HTTP en orden — cada SendAsync consume la siguiente. Captura las requests para asserts.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responses.Enqueue(responder);

    public void Enqueue(HttpStatusCode statusCode, string jsonBody) =>
        Enqueue(_ => new HttpResponseMessage(statusCode) { Content = new StringContent(jsonBody) });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException("No more fake responses queued.");

        return Task.FromResult(_responses.Dequeue()(request));
    }
}

internal sealed class FakeOAuthTokenManager(string accessToken = "fake-access-token") : IOAuthTokenManager
{
    public Task<Result<string>> GetValidAccessTokenAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(accessToken));
}

/// <summary>Sin espera real — para no ralentizar tests que no ejercitan la lógica de ventana/cooldown en sí.</summary>
internal sealed class NoWaitProviderRateLimiter : IProviderRateLimiter
{
    public List<(ProviderCode Provider, TimeSpan RetryAfter)> RecordedRateLimits { get; } = [];

    public Task WaitForSlotAsync(ProviderCode providerCode, CancellationToken ct = default) => Task.CompletedTask;

    public Task RecordRateLimitedAsync(ProviderCode providerCode, TimeSpan retryAfter, CancellationToken ct = default)
    {
        RecordedRateLimits.Add((providerCode, retryAfter));
        return Task.CompletedTask;
    }
}

internal sealed class FakeImapCredentialsRepository : IImapCredentialsRepository
{
    public List<ImapCredentials> Credentials { get; } = [];

    public Task AddAsync(ImapCredentials credentials, CancellationToken ct = default)
    {
        Credentials.Add(credentials);
        return Task.CompletedTask;
    }

    public Task<Result<ImapCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var found = Credentials.Find(c => c.AccountId == accountId);
        return Task.FromResult(
            found is null
                ? Result.Failure<ImapCredentials>(new Error("ImapCredentials.NotFound", "Not found."))
                : Result.Success(found)
        );
    }
}
