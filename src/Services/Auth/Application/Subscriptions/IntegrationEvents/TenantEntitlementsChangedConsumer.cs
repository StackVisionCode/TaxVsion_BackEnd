using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Proyecta los límites del plan (TenantPlanLimits) a partir del snapshot de
/// entitlements que Subscription recalcula tras cada alta, cambio de plan, compra de
/// seats, suspensión o reactivación. Único consumer — reemplaza a los antiguos
/// SubscriptionActivated/PlanChanged/Suspended/SeatsPurchased (retirados en la fase de
/// cleanup). Idempotente (upsert).
/// </summary>
public static class TenantEntitlementsChangedConsumer
{
    private const string ModuleFeaturePrefix = "module.";

    public static async Task Handle(
        TenantEntitlementsChangedIntegrationEvent evt,
        ITenantPlanLimitsStore planLimits,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var limits = await LoadOrCreateAsync(evt, planLimits, ct);
            ApplyEntitlements(limits, evt);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task<TenantPlanLimits> LoadOrCreateAsync(
        TenantEntitlementsChangedIntegrationEvent evt,
        ITenantPlanLimitsStore planLimits,
        CancellationToken ct
    )
    {
        var existing = await planLimits.GetAsync(evt.TenantId, ct);
        if (existing is not null)
            return existing;

        var created = TenantPlanLimits.Create(
            evt.TenantId,
            evt.PlanCode,
            maxUsers: 0,
            maxPendingInvitations: 0,
            storageQuotaBytes: 0,
            "[]"
        );
        await planLimits.AddAsync(created, ct);
        return created;
    }

    private static void ApplyEntitlements(TenantPlanLimits limits, TenantEntitlementsChangedIntegrationEvent evt)
    {
        var modulesJson = JsonSerializer.Serialize(ExtractEnabledModules(evt.EntitlementValues));

        limits.Apply(
            evt.PlanCode,
            maxUsers: evt.SeatCount,
            maxPendingInvitations: GetInt(evt.EntitlementValues, "invitations.max_pending", fallback: 0),
            storageQuotaBytes: GetLong(evt.EntitlementValues, "storage.max_bytes", fallback: 0),
            modulesJson
        );

        limits.SetSuspendedForBilling(evt.SubscriptionStatus == "Suspended");
    }

    private static string[] ExtractEnabledModules(IReadOnlyDictionary<string, string> entitlementValues)
    {
        var modules = new List<string>();
        foreach (var (key, value) in entitlementValues)
        {
            if (
                key.StartsWith(ModuleFeaturePrefix, StringComparison.Ordinal)
                && bool.TryParse(value, out var enabled)
                && enabled
            )
                modules.Add(key[ModuleFeaturePrefix.Length..]);
        }

        return modules.ToArray();
    }

    private static int GetInt(IReadOnlyDictionary<string, string> entitlementValues, string key, int fallback) =>
        entitlementValues.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) ? value : fallback;

    private static long GetLong(IReadOnlyDictionary<string, string> entitlementValues, string key, long fallback) =>
        entitlementValues.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : fallback;

    private static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
