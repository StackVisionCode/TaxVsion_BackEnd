using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Watch;

/// <summary>
/// Renueva una suscripción push antes de que expire. Mismo patrón que OAuthTokenManager (Fase 4):
/// llamada al provider → persistencia → en el 3er fallo consecutivo, MarkFailed + TenantEmailAccount
/// a Error, y el agregado levanta la alerta. WatchRenewalJob es el consumer boundary — este servicio
/// no pushea correlation propio, solo la lee ambient.
/// </summary>
public sealed class WatchRenewalService(
    IProviderWatchSubscriptionRepository subscriptionRepository,
    IWatchProviderClientFactory watchClientFactory,
    IUnitOfWork unitOfWork,
    IMailboxFailureUnit failureUnit,
    ICorrelationContext correlation,
    ILogger<WatchRenewalService> logger
) : IWatchRenewalService
{
    private const int MaxFailuresBeforeExpired = 3;

    public async Task<Result> RenewAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var subscriptionResult = await subscriptionRepository.GetByIdAsync(subscriptionId, ct);
        if (subscriptionResult.IsFailure)
            return Result.Failure(subscriptionResult.Error);

        var subscription = subscriptionResult.Value;
        var clientResult = watchClientFactory.Resolve(subscription.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        var now = DateTime.UtcNow;
        try
        {
            var renewed = await clientResult.Value.RenewWatchAsync(
                subscription.AccountId,
                subscription.SubscriptionRef,
                ct
            );
            subscription.Renew(renewed.SubscriptionRef, renewed.ExpiresAtUtc, now);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (WatchProviderException ex)
        {
            return await RecordFailureAsync(subscriptionId, ex, now, ct);
        }
    }

    public async Task<Result> RecreateAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var subscriptionResult = await subscriptionRepository.GetByIdAsync(subscriptionId, ct);
        if (subscriptionResult.IsFailure)
            return Result.Failure(subscriptionResult.Error);

        var subscription = subscriptionResult.Value;
        var clientResult = watchClientFactory.Resolve(subscription.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        var now = DateTime.UtcNow;
        try
        {
            var created = await clientResult.Value.SetupWatchAsync(subscription.AccountId, ct);
            subscription.Renew(created.SubscriptionRef, created.ExpiresAtUtc, now);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (WatchProviderException ex)
        {
            return await RecordFailureAsync(subscriptionId, ex, now, ct);
        }
    }

    private async Task<Result> RecordFailureAsync(
        Guid subscriptionId,
        WatchProviderException ex,
        DateTime now,
        CancellationToken ct
    )
    {
        var failures = 0;
        var correlationId = correlation.CorrelationId;
        await failureUnit.RunAsync(
            async (stores, token) =>
            {
                var found = await stores.Subscriptions.GetByIdAsync(subscriptionId, token);
                if (found.IsFailure)
                    return false;

                var subscription = found.Value;
                subscription.RecordRenewalFailure();
                failures = subscription.FailureCount;
                logger.LogWarning(
                    "Watch renewal failed for subscription {SubscriptionId} (attempt {FailureCount}): {Message}",
                    subscriptionId,
                    failures,
                    ex.Message
                );
                if (failures < MaxFailuresBeforeExpired)
                    return true;

                subscription.MarkFailed();
                var account = await stores.Accounts.GetByIdAsync(subscription.AccountId, token);
                // Si ya estaba en Error o desconectada, el usuario ya fue avisado.
                if (account.IsSuccess)
                    account.Value.MarkError(
                        new TenantEmailAccountErrorCause(
                            TenantEmailAccountErrorSource.WatchRenewal,
                            "WatchRenewalService.SubscriptionExpired",
                            ex.Message,
                            correlationId,
                            SubscriptionId: subscription.Id,
                            FailureCount: failures
                        ),
                        now
                    );
                return true;
            },
            ct
        );

        return failures < MaxFailuresBeforeExpired
            ? Result.Failure(new Error("WatchRenewalService.RenewalFailed", ex.Message))
            : Result.Failure(
                new Error(
                    "WatchRenewalService.SubscriptionExpired",
                    $"Watch subscription failed {failures} consecutive times."
                )
            );
    }
}
