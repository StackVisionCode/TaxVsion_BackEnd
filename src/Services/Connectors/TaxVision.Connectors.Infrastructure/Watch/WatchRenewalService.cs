using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Watch;
using Wolverine;

namespace TaxVision.Connectors.Infrastructure.Watch;

/// <summary>
/// Renueva una suscripción push antes de que expire. Mismo patrón que OAuthTokenManager (Fase 4):
/// llamada al provider → persistencia → en el 3er fallo consecutivo, MarkFailed + TenantEmailAccount
/// a Error + alerta (ConnectorsWatchExpiredIntegrationEvent). WatchRenewalJob es el consumer
/// boundary — este servicio no pushea correlation propio, solo la lee ambient.
/// </summary>
public sealed class WatchRenewalService(
    IProviderWatchSubscriptionRepository subscriptionRepository,
    ITenantEmailAccountRepository accountRepository,
    IWatchProviderClientFactory watchClientFactory,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
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
            return await RecordFailureAsync(subscription, ex, now, ct);
        }
    }

    private async Task<Result> RecordFailureAsync(
        ProviderWatchSubscription subscription,
        WatchProviderException ex,
        DateTime now,
        CancellationToken ct
    )
    {
        subscription.RecordRenewalFailure();
        logger.LogWarning(
            "Watch renewal failed for subscription {SubscriptionId} (attempt {FailureCount}): {Message}",
            subscription.Id,
            subscription.FailureCount,
            ex.Message
        );

        if (subscription.FailureCount < MaxFailuresBeforeExpired)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(new Error("WatchRenewalService.RenewalFailed", ex.Message));
        }

        subscription.MarkFailed();

        var tenantId = Guid.Empty;
        var accountResult = await accountRepository.GetByIdAsync(subscription.AccountId, ct);
        if (accountResult.IsSuccess)
        {
            accountResult.Value.MarkError(now);
            tenantId = accountResult.Value.TenantId;
        }

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new ConnectorsWatchExpiredIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlation.CorrelationId,
                AccountId = subscription.AccountId,
                SubscriptionId = subscription.Id,
                ProviderCode = subscription.ProviderCode.ToString(),
                FailureCount = subscription.FailureCount,
                ExpiredAtUtc = now,
            }
        );

        return Result.Failure(
            new Error(
                "WatchRenewalService.SubscriptionExpired",
                $"Watch subscription failed {subscription.FailureCount} consecutive times."
            )
        );
    }
}
