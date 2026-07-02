using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Proyecta los límites del plan (TenantPlanLimits) a partir de los eventos del
/// servicio Subscription. Todos los handlers son idempotentes (upsert).
/// </summary>
public static class SubscriptionActivatedConsumer
{
    public static async Task Handle(
        SubscriptionActivatedIntegrationEvent evt,
        ITenantPlanLimitsStore planLimits,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var modulesJson = JsonSerializer.Serialize(evt.EnabledModules);
            var limits = await planLimits.GetAsync(evt.SubscribedTenantId, ct);
            if (limits is null)
            {
                limits = TenantPlanLimits.Create(
                    evt.SubscribedTenantId,
                    evt.PlanCode,
                    evt.MaxUsers,
                    evt.MaxPendingInvitations,
                    evt.StorageQuotaBytes,
                    modulesJson);
                await planLimits.AddAsync(limits, ct);
            }
            else
            {
                limits.Apply(
                    evt.PlanCode,
                    evt.MaxUsers,
                    evt.MaxPendingInvitations,
                    evt.StorageQuotaBytes,
                    modulesJson);
            }

            limits.SetSuspendedForBilling(false);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    internal static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}

public static class SubscriptionPlanChangedConsumer
{
    public static async Task Handle(
        SubscriptionPlanChangedIntegrationEvent evt,
        ITenantPlanLimitsStore planLimits,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(
            SubscriptionActivatedConsumer.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var modulesJson = JsonSerializer.Serialize(evt.EnabledModules);
            var limits = await planLimits.GetAsync(evt.SubscribedTenantId, ct);
            if (limits is null)
            {
                limits = TenantPlanLimits.Create(
                    evt.SubscribedTenantId,
                    evt.PlanCode,
                    evt.MaxUsers,
                    evt.MaxPendingInvitations,
                    evt.StorageQuotaBytes,
                    modulesJson);
                await planLimits.AddAsync(limits, ct);
            }
            else
            {
                limits.Apply(
                    evt.PlanCode,
                    evt.MaxUsers,
                    evt.MaxPendingInvitations,
                    evt.StorageQuotaBytes,
                    modulesJson);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

public static class SubscriptionSuspendedConsumer
{
    public static async Task Handle(
        SubscriptionSuspendedIntegrationEvent evt,
        ITenantPlanLimitsStore planLimits,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(
            SubscriptionActivatedConsumer.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var limits = await planLimits.GetAsync(evt.SubscribedTenantId, ct);
            if (limits is not null)
            {
                limits.SetSuspendedForBilling(true);
                await unitOfWork.SaveChangesAsync(ct);
            }
        }
    }
}

public static class SeatsPurchasedConsumer
{
    public static async Task Handle(
        SeatsPurchasedIntegrationEvent evt,
        ITenantPlanLimitsStore planLimits,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(
            SubscriptionActivatedConsumer.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var limits = await planLimits.GetAsync(evt.PurchasingTenantId, ct);
            if (limits is not null)
            {
                limits.SetMaxUsers(evt.NewMaxUsers);
                await unitOfWork.SaveChangesAsync(ct);
            }
        }
    }
}
