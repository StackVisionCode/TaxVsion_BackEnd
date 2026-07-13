using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

/// <summary>
/// Proyecta el límite de almacenamiento (TenantStorageLimit) a partir del snapshot de
/// entitlements que Subscription recalcula tras cada alta, cambio de plan o suspensión.
/// Único consumer — reemplaza a los antiguos SubscriptionActivated/PlanChanged/Suspended
/// (retirados en la fase de cleanup). Idempotente (upsert).
/// </summary>
public static class TenantEntitlementsChangedQuotaConsumer
{
    public static async Task Handle(
        TenantEntitlementsChangedIntegrationEvent message,
        IStorageLimitRepository repository,
        IOptions<CloudStorageOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var storageQuotaBytes = GetLong(message.EntitlementValues, "storage.max_bytes", fallback: 0);
        var isSuspended = message.SubscriptionStatus == "Suspended";

        await ApplyAsync(message.TenantId, message.PlanCode, storageQuotaBytes, isSuspended, repository, options.Value, unitOfWork, ct);
    }

    private static async Task ApplyAsync(
        Guid tenantId,
        string planCode,
        long storageQuotaBytes,
        bool isSuspended,
        IStorageLimitRepository repository,
        CloudStorageOptions options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var maxBytes = storageQuotaBytes > 0 ? storageQuotaBytes : options.DefaultStorageQuotaBytes;
        var maxFileSizeBytes = options.ResolvePlanPolicy(planCode).MaxFileSizeBytes;

        var existing = await repository.GetAsync(tenantId, ct);
        if (existing is null)
        {
            existing = TenantStorageLimit.Create(tenantId, planCode, maxBytes, maxFileSizeBytes);
            repository.Add(existing);
        }
        else
        {
            existing.ApplyPlan(planCode, maxBytes, maxFileSizeBytes);
        }

        if (isSuspended)
            existing.Suspend();

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static long GetLong(IReadOnlyDictionary<string, string> entitlementValues, string key, long fallback) =>
        entitlementValues.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : fallback;
}
