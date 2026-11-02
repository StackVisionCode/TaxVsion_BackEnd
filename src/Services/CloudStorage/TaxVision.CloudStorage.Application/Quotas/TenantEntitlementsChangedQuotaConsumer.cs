using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.FileTypes;
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
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(message.CorrelationId) ? message.EventId.ToString("N") : message.CorrelationId
        );

        var storageQuotaBytes = GetLong(message.EntitlementValues, "storage.max_bytes", fallback: 0);
        // Lo que vende el plan en archivos (Subscription StorageEntitlementCatalog). Sin la clave = tier mínimo.
        var maxFileBytes = GetLong(message.EntitlementValues, "storage.max_file_bytes", PlanFileAllowanceDefaults.MaxFileBytes);
        var fileTypeGroups = message.EntitlementValues.TryGetValue("storage.file_type_groups", out var groups)
            && !string.IsNullOrWhiteSpace(groups)
            ? groups
            : PlanFileAllowanceDefaults.FileTypeGroups;
        var isSuspended = message.SubscriptionStatus == "Suspended";

        await ApplyAsync(
            message.TenantId,
            message.PlanCode,
            storageQuotaBytes,
            maxFileBytes,
            fileTypeGroups,
            isSuspended,
            repository,
            options.Value,
            unitOfWork,
            ct
        );
    }

    private static async Task ApplyAsync(
        Guid tenantId,
        string planCode,
        long storageQuotaBytes,
        long maxFileBytes,
        string fileTypeGroups,
        bool isSuspended,
        IStorageLimitRepository repository,
        CloudStorageOptions options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var maxBytes = storageQuotaBytes > 0 ? storageQuotaBytes : options.DefaultStorageQuotaBytes;

        var existing = await repository.GetAsync(tenantId, ct);
        if (existing is null)
        {
            existing = TenantStorageLimit.Create(tenantId, planCode, maxBytes, maxFileBytes, fileTypeGroups);
            repository.Add(existing);
        }
        else
        {
            existing.ApplyPlan(planCode, maxBytes, maxFileBytes, fileTypeGroups);
        }

        if (isSuspended)
            existing.Suspend();

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static long GetLong(IReadOnlyDictionary<string, string> entitlementValues, string key, long fallback) =>
        entitlementValues.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : fallback;
}
