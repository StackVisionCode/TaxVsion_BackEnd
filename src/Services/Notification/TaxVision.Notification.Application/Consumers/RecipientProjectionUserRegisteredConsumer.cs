using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Application.Consumers;

public static class RecipientProjectionUserRegisteredConsumer
{
    private const string TenantAdmin = "TenantAdmin";
    private const string CustomerPortal = "CustomerPortal";

    public static async Task Handle(
        UserRegisteredIntegrationEvent evt,
        ITenantOwnerProjectionRepository owners,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (string.Equals(evt.ActorType, TenantAdmin, StringComparison.OrdinalIgnoreCase))
            {
                await UpsertOwnerAsync(owners, evt.TenantId, evt.UserId, evt.OccurredOn, ct);
                await unitOfWork.SaveChangesAsync(ct);
            }
            else if (
                string.Equals(evt.ActorType, CustomerPortal, StringComparison.OrdinalIgnoreCase)
                && evt.CustomerId is { } customerId
            )
            {
                await UpsertPortalAsync(portalAccounts, evt.TenantId, customerId, evt.UserId, ct);
                await unitOfWork.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task UpsertOwnerAsync(
        ITenantOwnerProjectionRepository owners,
        Guid tenantId,
        Guid userId,
        DateTime registeredAtUtc,
        CancellationToken ct
    )
    {
        var existing = await owners.GetByUserAsync(tenantId, userId, ct);
        if (existing is null)
            await owners.AddAsync(TenantOwnerProjection.Create(tenantId, userId, registeredAtUtc), ct);
        else
            existing.Synchronize(registeredAtUtc, true);
    }

    private static async Task UpsertPortalAsync(
        ICustomerPortalAccountProjectionRepository portalAccounts,
        Guid tenantId,
        Guid customerId,
        Guid userId,
        CancellationToken ct
    )
    {
        var existing = await portalAccounts.GetByCustomerAsync(tenantId, customerId, ct);
        if (existing is null)
            await portalAccounts.AddAsync(CustomerPortalAccountProjection.Create(tenantId, customerId, userId), ct);
        else
            existing.MarkActive(userId);
    }
}
