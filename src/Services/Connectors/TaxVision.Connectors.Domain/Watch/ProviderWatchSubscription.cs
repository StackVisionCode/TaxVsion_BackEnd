using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Watch;

public enum ProviderWatchStatus
{
    Active,
    Expired,
    Failed,
    Removed,
}

/// <summary>
/// Suscripción push activa con Gmail (Pub/Sub <c>users.watch</c>) o Graph (webhook subscription)
/// para un TenantEmailAccount — 1:1 por AccountId. IMAP no tiene equivalente (sin mecanismo de push
/// genérico): esas cuentas nunca tienen una fila acá, se activan directo (ver SetupWatchHandler).
/// </summary>
public sealed class ProviderWatchSubscription : BaseEntity
{
    private ProviderWatchSubscription() { }

    public Guid AccountId { get; private set; }
    public ProviderCode ProviderCode { get; private set; }
    public string SubscriptionRef { get; private set; } = default!;
    public string? TopicName { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime LastRenewedAtUtc { get; private set; }
    public ProviderWatchStatus Status { get; private set; }
    public int FailureCount { get; private set; }

    public static Result<ProviderWatchSubscription> Create(
        Guid accountId,
        ProviderCode providerCode,
        string subscriptionRef,
        string? topicName,
        DateTime expiresAtUtc,
        DateTime nowUtc
    )
    {
        if (accountId == Guid.Empty)
            return Result.Failure<ProviderWatchSubscription>(
                new Error("ProviderWatchSubscription.AccountId", "AccountId is required.")
            );

        if (string.IsNullOrWhiteSpace(subscriptionRef) || subscriptionRef.Length > 500)
            return Result.Failure<ProviderWatchSubscription>(
                new Error(
                    "ProviderWatchSubscription.SubscriptionRef",
                    "SubscriptionRef is required and must be at most 500 chars."
                )
            );

        if (expiresAtUtc <= nowUtc)
            return Result.Failure<ProviderWatchSubscription>(
                new Error("ProviderWatchSubscription.ExpiresAtUtc", "ExpiresAtUtc must be in the future.")
            );

        return Result.Success(
            new ProviderWatchSubscription
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ProviderCode = providerCode,
                SubscriptionRef = subscriptionRef,
                TopicName = topicName,
                ExpiresAtUtc = expiresAtUtc,
                LastRenewedAtUtc = nowUtc,
                Status = ProviderWatchStatus.Active,
                FailureCount = 0,
            }
        );
    }

    /// <summary>Setup o renewal exitoso — reemplaza la referencia del proveedor y resetea el contador de fallos.</summary>
    public void Renew(string subscriptionRef, DateTime expiresAtUtc, DateTime renewedAtUtc)
    {
        SubscriptionRef = subscriptionRef;
        ExpiresAtUtc = expiresAtUtc;
        LastRenewedAtUtc = renewedAtUtc;
        Status = ProviderWatchStatus.Active;
        FailureCount = 0;
    }

    /// <summary>Un intento de renewal falló — no necesariamente terminal (ver WatchRenewalService, 3 strikes).</summary>
    public void RecordRenewalFailure() => FailureCount++;

    /// <summary>Se agotaron los reintentos (3 fallos consecutivos, Fase 6) — requiere reauth manual.</summary>
    public void MarkFailed() => Status = ProviderWatchStatus.Failed;
}
