using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
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
    IProviderWatchSubscriptionRepository subscriptionRepository,
    IOAuthProviderClientFactory providerClientFactory,
    HttpResiliencePipelineRegistry circuitBreakers,
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
                    OAuthTokenErrorCodes.LockTimeout,
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
                new Error(OAuthTokenErrorCodes.NoToken, $"OAuthConnection {connection.Id} has no token attached.")
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

        OAuthTokenGrant? grant;
        OAuthProviderException? rejection;
        try
        {
            (grant, rejection) = await breaker.ExecuteAsync(
                async token =>
                {
                    // El rechazo de una cuenta se devuelve en vez de lanzarse: el breaker es del
                    // proveedor y solo debe abrirse por caídas. Antes, una cuenta muerta lo abría a
                    // los 3 intentos y bloqueaba el refresh de las cuentas sanas durante 60 s.
                    try
                    {
                        return (
                            await clientResult.Value.RefreshAccessTokenAsync(refreshTokenPlaintext, token),
                            (OAuthProviderException?)null
                        );
                    }
                    catch (OAuthProviderException ex) when (ex.RequiresReconnect)
                    {
                        return ((OAuthTokenGrant?)null, ex);
                    }
                },
                ct
            );
        }
        catch (BrokenCircuitException ex)
        {
            // El breaker es por proveedor y lo comparten todas las cuentas: estar abierto no dice
            // nada de ESTA cuenta. Es transitorio, así que no se marca Error ni se avisa al usuario.
            logger.LogWarning(
                "OAuth refresh skipped for account {AccountId} — {Provider} circuit breaker is open.",
                accountId,
                connection.ProviderCode
            );
            return Result.Failure<string>(new Error(OAuthTokenErrorCodes.CircuitOpen, ex.Message));
        }
        catch (OAuthProviderException ex)
        {
            // Caída del proveedor (5xx, 429, red) o credenciales de la app: no es culpa de la cuenta
            // ni se arregla reconectando. Se reintenta en el próximo push o en la próxima corrida del job.
            logger.LogWarning(
                "OAuth refresh for account {AccountId} failed transiently: {Message}",
                accountId,
                ex.Message
            );
            return Result.Failure<string>(new Error(OAuthTokenErrorCodes.ProviderUnavailable, ex.Message));
        }

        if (rejection is not null)
        {
            var error = new Error(OAuthTokenErrorCodes.RefreshFailed, rejection.Message);
            await MarkAccountErrorAndPublishAsync(accountId, connection, error, ct);
            return Result.Failure<string>(error);
        }

        var granted = grant!;
        var now = DateTime.UtcNow;
        connection.Token.UpdateAccessToken(
            protector.Protect(granted.AccessToken),
            now.AddSeconds(granted.ExpiresInSeconds),
            now
        );
        if (granted.RefreshToken is not null)
            connection.Token.UpdateRefreshToken(protector.Protect(granted.RefreshToken));

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(granted.AccessToken);
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
            // Se avisa solo al pasar a Error. Si ya estaba en Error (o la desconectaron), el usuario
            // ya fue avisado: publicar en cada intento creaba una notificación por intento.
            if (accountResult.Value.Status is TenantEmailAccountStatus.Error or TenantEmailAccountStatus.Disconnected)
                return;

            accountResult.Value.MarkError(DateTime.UtcNow);
            await StopWatchRenewalAsync(accountId, ct);
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
                CreatedByUserId = accountResult.IsSuccess ? accountResult.Value.CreatedByUserId : Guid.Empty,
                IsOfficeMailbox = accountResult.IsSuccess ? accountResult.Value.IsOffice : null,
                MailboxOwnerUserId = accountResult.IsSuccess ? accountResult.Value.OwnerUserId : null,
            }
        );
    }

    /// <summary>
    /// Sin token no se puede llamar a <c>users.stop</c> de Gmail. Marcar el watch como Failed corta
    /// las renovaciones (WatchRenewalJob ignora Failed), así que Google deja de mandar pushes cuando
    /// el watch vence. Reconectar la cuenta lo reactiva (WatchActivationService → Renew).
    /// </summary>
    private async Task StopWatchRenewalAsync(Guid accountId, CancellationToken ct)
    {
        var subscriptionResult = await subscriptionRepository.GetByAccountIdAsync(accountId, ct);
        if (subscriptionResult.IsSuccess)
            subscriptionResult.Value.MarkFailed();
    }
}
