using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.RateLimit;
using Wolverine;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>
/// Orquesta el refresh proactivo (Fase 4): cache check → lock distribuido con backoff (evita 2
/// nodos refrescando el mismo token) → refresh contra el provider (circuit breaker) → persistencia.
/// El caller (consumer Wolverine o ProactiveTokenRefreshJob) es responsable de pushear el
/// correlation id — este método no pushea uno propio, solo lo lee para el evento de falla.
/// </summary>
public sealed class OAuthTokenManager(
    ITenantEmailAccountRepository accountRepository,
    IOAuthConnectionRepository connectionRepository,
    IOAuthProviderClientFactory providerClientFactory,
    ProviderCircuitBreakerRegistry circuitBreakers,
    IEncryptedSecretProtector protector,
    IDistributedLock distributedLock,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    ICorrelationContext correlation,
    ILogger<OAuthTokenManager> logger
) : IOAuthTokenManager
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);
    private static readonly int[] BackoffDelaysMs = [200, 400, 800, 1600, 3200];

    public async Task<Result<string>> GetValidAccessTokenAsync(Guid accountId, CancellationToken ct = default)
    {
        var connectionResult = await connectionRepository.GetByAccountIdAsync(accountId, ct);
        if (connectionResult.IsFailure)
            return Result.Failure<string>(connectionResult.Error);

        var freshTokenResult = TryGetFreshAccessToken(connectionResult.Value);
        if (freshTokenResult is not null)
            return freshTokenResult;

        var lockKey = $"connectors:oauth-refresh:{accountId:N}";
        await using var lockHandle = await AcquireWithBackoffAsync(lockKey, ct);
        if (lockHandle is null)
            return Result.Failure<string>(
                new Error(
                    "OAuthTokenManager.LockTimeout",
                    $"Timed out waiting for the refresh lock on account {accountId}."
                )
            );

        // Double-checked: otro nodo puede haber refrescado mientras esperábamos el lock.
        connectionResult = await connectionRepository.GetByAccountIdAsync(accountId, ct);
        if (connectionResult.IsFailure)
            return Result.Failure<string>(connectionResult.Error);

        freshTokenResult = TryGetFreshAccessToken(connectionResult.Value);
        if (freshTokenResult is not null)
            return freshTokenResult;

        return await RefreshAsync(accountId, connectionResult.Value, ct);
    }

    /// <summary>Devuelve el access token vigente sin refrescar, o null si expira dentro de <see cref="RefreshBuffer"/>.</summary>
    private Result<string>? TryGetFreshAccessToken(OAuthConnection connection)
    {
        if (connection.Token is null)
            return Result.Failure<string>(
                new Error("OAuthTokenManager.NoToken", $"OAuthConnection {connection.Id} has no token attached.")
            );

        if (connection.Token.AccessTokenExpiresAtUtc > DateTime.UtcNow.Add(RefreshBuffer))
            return Result.Success(protector.Unprotect(connection.Token.AccessTokenCipher));

        return null;
    }

    private async Task<ILockHandle?> AcquireWithBackoffAsync(string key, CancellationToken ct)
    {
        foreach (var delayMs in BackoffDelaysMs)
        {
            var handle = await distributedLock.AcquireAsync(key, LockTtl, ct);
            if (handle.IsAcquired)
                return handle;

            await handle.DisposeAsync();
            await Task.Delay(delayMs, ct);
        }
        return null;
    }

    private async Task<Result<string>> RefreshAsync(Guid accountId, OAuthConnection connection, CancellationToken ct)
    {
        var clientResult = providerClientFactory.Resolve(connection.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<string>(clientResult.Error);

        var refreshTokenPlaintext = connection.Token!.RefreshTokenCipher.Decrypt(protector);
        var breaker = circuitBreakers.GetOrCreate($"{connection.ProviderCode}:oauth-refresh");

        OAuthTokenGrant grant;
        try
        {
            grant = await breaker.ExecuteAsync(
                token => clientResult.Value.RefreshAccessTokenAsync(refreshTokenPlaintext, token),
                ct
            );
        }
        catch (Exception ex) when (ex is OAuthProviderException or BrokenCircuitException)
        {
            var error = new Error("OAuthTokenManager.RefreshFailed", ex.Message);
            await MarkAccountErrorAndPublishAsync(accountId, connection, error, ct);
            return Result.Failure<string>(error);
        }

        var now = DateTime.UtcNow;
        connection.Token.UpdateAccessToken(
            protector.Protect(grant.AccessToken),
            now.AddSeconds(grant.ExpiresInSeconds),
            now
        );
        if (grant.RefreshToken is not null)
            connection.Token.UpdateRefreshToken(protector.Protect(grant.RefreshToken));

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(grant.AccessToken);
    }

    private async Task MarkAccountErrorAndPublishAsync(
        Guid accountId,
        OAuthConnection connection,
        Error error,
        CancellationToken ct
    )
    {
        logger.LogWarning(
            "OAuth refresh failed for account {AccountId}: {ErrorCode} {Message}",
            accountId,
            error.Code,
            error.Message
        );

        var accountResult = await accountRepository.GetByIdAsync(accountId, ct);
        if (accountResult.IsSuccess)
        {
            accountResult.Value.MarkError(DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
        }

        await bus.PublishAsync(
            new ConnectorsOAuthRefreshFailedIntegrationEvent
            {
                TenantId = accountResult.IsSuccess ? accountResult.Value.TenantId : Guid.Empty,
                CorrelationId = correlation.CorrelationId,
                AccountId = accountId,
                ConnectionId = connection.Id,
                ProviderCode = connection.ProviderCode.ToString(),
                Reason = error.Message,
                ErrorCode = error.Code,
                FailedAtUtc = DateTime.UtcNow,
            }
        );
    }
}
