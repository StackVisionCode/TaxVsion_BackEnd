using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

public static class SubscriptionActivatedQuotaConsumer
{
    public static Task Handle(
        SubscriptionActivatedIntegrationEvent message,
        IStorageLimitRepository repository,
        IOptions<CloudStorageOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        ApplyAsync(
            message.SubscribedTenantId,
            message.PlanCode,
            message.StorageQuotaBytes,
            repository,
            options.Value,
            unitOfWork,
            ct
        );

    internal static async Task ApplyAsync(
        Guid tenantId,
        string planCode,
        long storageQuotaBytes,
        IStorageLimitRepository repository,
        CloudStorageOptions options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var maxBytes = storageQuotaBytes > 0 ? storageQuotaBytes : options.DefaultStorageQuotaBytes;
        var existing = await repository.GetAsync(tenantId, ct);
        if (existing is null)
            repository.Add(
                TenantStorageLimit.Create(
                    tenantId,
                    planCode,
                    maxBytes,
                    options.ResolvePlanPolicy(planCode).MaxFileSizeBytes
                )
            );
        else
            existing.ApplyPlan(planCode, maxBytes, options.ResolvePlanPolicy(planCode).MaxFileSizeBytes);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

public static class SubscriptionPlanChangedQuotaConsumer
{
    public static Task Handle(
        SubscriptionPlanChangedIntegrationEvent message,
        IStorageLimitRepository repository,
        IOptions<CloudStorageOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        SubscriptionActivatedQuotaConsumer.ApplyAsync(
            message.SubscribedTenantId,
            message.PlanCode,
            message.StorageQuotaBytes,
            repository,
            options.Value,
            unitOfWork,
            ct
        );
}

public static class SubscriptionSuspendedQuotaConsumer
{
    public static async Task Handle(
        SubscriptionSuspendedIntegrationEvent message,
        IStorageLimitRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existing = await repository.GetAsync(message.SubscribedTenantId, ct);
        if (existing is null)
            return;
        existing.Suspend();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
