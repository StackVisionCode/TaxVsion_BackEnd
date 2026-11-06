using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts.DomainEvents;

/// <summary>
/// La cuenta pasó a Error: devuelve el aviso de integración (cascada por el outbox). Corre después del commit, desde
/// la cola domain-events. Todo lo que lleva el aviso viene en el evento; no relee el agregado.
/// </summary>
public static class TenantEmailAccountErroredDomainEventHandler
{
    public static IntegrationEvent Handle(TenantEmailAccountErroredDomainEvent errored) =>
        errored.Cause.Source == TenantEmailAccountErrorSource.WatchRenewal
            ? WatchExpired(errored)
            : RefreshFailed(errored);

    // Para el usuario, el refresh OAuth y el rechazo de IMAP son lo mismo: "reconecta tu buzón".
    private static ConnectorsOAuthRefreshFailedIntegrationEvent RefreshFailed(
        TenantEmailAccountErroredDomainEvent errored
    ) =>
        new()
        {
            TenantId = errored.TenantId,
            CorrelationId = errored.Cause.CorrelationId,
            AccountId = errored.AccountId,
            ConnectionId = errored.Cause.ConnectionId,
            ProviderCode = errored.ProviderCode.ToString(),
            Reason = errored.Cause.Reason,
            ErrorCode = errored.Cause.ErrorCode,
            FailedAtUtc = errored.OccurredAtUtc,
            CreatedByUserId = errored.CreatedByUserId,
            IsOfficeMailbox = errored.IsOfficeMailbox,
            MailboxOwnerUserId = errored.MailboxOwnerUserId,
        };

    private static ConnectorsWatchExpiredIntegrationEvent WatchExpired(TenantEmailAccountErroredDomainEvent errored) =>
        new()
        {
            TenantId = errored.TenantId,
            CorrelationId = errored.Cause.CorrelationId,
            AccountId = errored.AccountId,
            SubscriptionId = errored.Cause.SubscriptionId,
            ProviderCode = errored.ProviderCode.ToString(),
            FailureCount = errored.Cause.FailureCount,
            ExpiredAtUtc = errored.OccurredAtUtc,
            CreatedByUserId = errored.CreatedByUserId,
            IsOfficeMailbox = errored.IsOfficeMailbox,
            MailboxOwnerUserId = errored.MailboxOwnerUserId,
        };
}
