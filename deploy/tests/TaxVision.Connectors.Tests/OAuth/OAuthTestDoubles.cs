using System.Collections.Concurrent;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Connectors.Tests.OAuth;

internal sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];
    public List<object> Invoked { get; } = [];

    /// <summary>Resultado devuelto por InvokeAsync&lt;Result&gt; — único tipo genérico que este fake soporta, porque es el único que los handlers de Connectors invocan vía bus.</summary>
    public Result InvokeResult { get; set; } = Result.Success();

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
            Published.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotImplementedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public string? TenantId
    {
        get => null;
        set { }
    }

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        if (message is not null)
            Invoked.Add(message);

        if (typeof(T) == typeof(Result))
            return Task.FromResult((T)(object)InvokeResult);

        throw new NotImplementedException();
    }

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var previous = CorrelationId;
        CorrelationId = correlationId;
        return new Popper(this, previous);
    }

    private sealed class Popper(FakeCorrelationContext owner, string previous) : IDisposable
    {
        public void Dispose() => owner.CorrelationId = previous;
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeEncryptedSecretProtector : IEncryptedSecretProtector
{
    public EncryptedSecret Protect(string plaintext, short? keyVersion = null) =>
        EncryptedSecret.Create(Encoding.UTF8.GetBytes(plaintext), new byte[12], new byte[16], keyVersion ?? 1).Value;

    public string Unprotect(EncryptedSecret secret) => Encoding.UTF8.GetString(secret.Ciphertext);
}

internal sealed class FakeTenantEmailAccountRepository : ITenantEmailAccountRepository
{
    public List<TenantEmailAccount> Accounts { get; } = [];

    public Task AddAsync(TenantEmailAccount account, CancellationToken ct = default)
    {
        Accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task<Result<TenantEmailAccount>> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = Accounts.Find(a => a.Id == accountId);
        return Task.FromResult(
            account is null
                ? Result.Failure<TenantEmailAccount>(new Error("TenantEmailAccount.NotFound", "Not found."))
                : Result.Success(account)
        );
    }

    public Task<Result<TenantEmailAccount>> GetByEmailAddressAsync(string emailAddress, CancellationToken ct = default)
    {
        var normalized = emailAddress.Trim().ToLowerInvariant();
        var account = Accounts.Find(a => a.EmailAddress == normalized);
        return Task.FromResult(
            account is null
                ? Result.Failure<TenantEmailAccount>(new Error("TenantEmailAccount.NotFound", "Not found."))
                : Result.Success(account)
        );
    }

    public Task<IReadOnlyList<TenantEmailAccount>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        IReadOnlyList<TenantEmailAccount> accounts = Accounts.FindAll(a => a.TenantId == tenantId);
        return Task.FromResult(accounts);
    }

    public Task<IReadOnlyList<TenantEmailAccount>> ListActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TenantEmailAccount> accounts = Accounts.FindAll(a => a.Status == TenantEmailAccountStatus.Active);
        return Task.FromResult(accounts);
    }
}

internal sealed class FakeOAuthConnectionRepository : IOAuthConnectionRepository
{
    public List<OAuthConnection> Connections { get; } = [];

    public Task AddAsync(OAuthConnection connection, CancellationToken ct = default)
    {
        Connections.Add(connection);
        return Task.CompletedTask;
    }

    public Task<Result<OAuthConnection>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var connection = Connections.Find(c => c.AccountId == accountId);
        return Task.FromResult(
            connection is null
                ? Result.Failure<OAuthConnection>(new Error("OAuthConnection.NotFound", "Not found."))
                : Result.Success(connection)
        );
    }

    public Task<IReadOnlyList<Guid>> ListAccountIdsWithTokenExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<Guid> ids = Connections
            .Where(c =>
                c.Status == OAuthConnectionStatus.Active
                && c.Token is not null
                && c.Token.AccessTokenExpiresAtUtc < thresholdUtc
            )
            .Select(c => c.AccountId)
            .ToList();
        return Task.FromResult(ids);
    }
}

internal sealed class FakeOAuthProviderClient(ProviderCode providerCode) : IOAuthProviderClient
{
    private int _callCount;

    public ProviderCode ProviderCode { get; } = providerCode;
    public string ClientId { get; set; } = "fake-client-id";
    public string ConfiguredScope { get; set; } = "fake-scope";
    public string RedirectUri { get; set; } = "https://app.example.com/callback";
    public int CallCount => _callCount;
    public List<string> RevokedRefreshTokens { get; } = [];
    public Func<string, OAuthTokenGrant>? OnRefresh { get; set; }
    public Exception? ThrowOnRefresh { get; set; }
    public Func<string, string, OAuthTokenGrant>? OnExchange { get; set; }
    public Exception? ThrowOnExchange { get; set; }
    public Func<string, string>? OnGetAuthorizedEmailAddress { get; set; }
    public Exception? ThrowOnGetAuthorizedEmailAddress { get; set; }

    public string BuildAuthorizationUrl(string state) => $"https://provider.example.com/authorize?state={state}";

    public Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        RevokedRefreshTokens.Add(refreshToken);
        return Task.CompletedTask;
    }

    public Task<OAuthTokenGrant> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        if (ThrowOnRefresh is not null)
            throw ThrowOnRefresh;

        return Task.FromResult(OnRefresh?.Invoke(refreshToken) ?? new OAuthTokenGrant("new-access-token", null, 3600));
    }

    public Task<OAuthTokenGrant> ExchangeAuthorizationCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        Interlocked.Increment(ref _callCount);
        if (ThrowOnExchange is not null)
            throw ThrowOnExchange;

        return Task.FromResult(
            OnExchange?.Invoke(code, redirectUri) ?? new OAuthTokenGrant("new-access-token", "new-refresh-token", 3600)
        );
    }

    public Task<string> GetAuthorizedEmailAddressAsync(string accessToken, CancellationToken ct = default)
    {
        if (ThrowOnGetAuthorizedEmailAddress is not null)
            throw ThrowOnGetAuthorizedEmailAddress;

        return Task.FromResult(OnGetAuthorizedEmailAddress?.Invoke(accessToken) ?? "connected@example.com");
    }
}

internal sealed class FakeOAuthProviderClientFactory(IOAuthProviderClient client) : IOAuthProviderClientFactory
{
    public Result<IOAuthProviderClient> Resolve(ProviderCode providerCode) => Result.Success(client);
}

internal sealed class FakeMicrosoftAdminConsentClient : IMicrosoftAdminConsentClient
{
    public string BuildAdminConsentUrl(string state) => $"https://provider.example.com/adminconsent?state={state}";
}

internal sealed class FakeProviderConnectionAuditLogRepository : IProviderConnectionAuditLogRepository
{
    public List<ProviderConnectionAuditLog> Entries { get; } = [];

    public Task AddAsync(ProviderConnectionAuditLog entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
        Task.FromResult(0);
}

/// <summary>SET-NX-like lock en memoria — semántica real de "solo un caller a la vez" para tests de concurrencia.</summary>
internal sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, byte> _held = new();

    public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ILockHandle handle = _held.TryAdd(key, 0) ? new Handle(this, key) : new UnacquiredHandle(key);
        return Task.FromResult(handle);
    }

    private sealed class Handle(InMemoryDistributedLock owner, string key) : ILockHandle
    {
        public bool IsAcquired => true;
        public string Key => key;

        public ValueTask DisposeAsync()
        {
            owner._held.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnacquiredHandle(string key) : ILockHandle
    {
        public bool IsAcquired => false;
        public string Key { get; } = key;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
