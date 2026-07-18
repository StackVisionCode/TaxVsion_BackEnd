using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Watch;

public sealed record WatchSetupResult(string SubscriptionRef, string? TopicName, DateTime ExpiresAtUtc);

public sealed class WatchProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Establece/renueva la suscripción push del proveedor (Gmail <c>users.watch</c> / Graph webhook
/// subscriptions). Solo Gmail y Graph la implementan — IMAP no tiene equivalente de push genérico
/// (ver WatchProviderClientFactory, D1 §34.5).
/// </summary>
public interface IWatchProviderClient
{
    ProviderCode ProviderCode { get; }

    Task<WatchSetupResult> SetupWatchAsync(Guid accountId, CancellationToken ct = default);

    Task<WatchSetupResult> RenewWatchAsync(Guid accountId, string subscriptionRef, CancellationToken ct = default);
}

public interface IWatchProviderClientFactory
{
    Result<IWatchProviderClient> Resolve(ProviderCode providerCode);
}
