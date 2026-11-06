using BuildingBlocks.Domain;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>Qué sacó a la cuenta de servicio. Decide el aviso de integración.</summary>
public enum TenantEmailAccountErrorSource
{
    OAuthRefresh,
    ImapCredentials,
    WatchRenewal,
}

/// <summary>Por qué la cuenta pasó a Error. <paramref name="CorrelationId"/> es el de quien detectó el fallo.</summary>
public sealed record TenantEmailAccountErrorCause(
    TenantEmailAccountErrorSource Source,
    string ErrorCode,
    string Reason,
    string CorrelationId,
    Guid ConnectionId = default,
    Guid SubscriptionId = default,
    int FailureCount = 0
);

/// <summary>La cuenta pasó a Error. Lleva la foto de ese momento: el aviso no relee el agregado.</summary>
public sealed record TenantEmailAccountErroredDomainEvent(
    Guid TenantId,
    Guid AccountId,
    ProviderCode ProviderCode,
    TenantEmailAccountErrorCause Cause,
    Guid CreatedByUserId,
    bool IsOfficeMailbox,
    Guid? MailboxOwnerUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
