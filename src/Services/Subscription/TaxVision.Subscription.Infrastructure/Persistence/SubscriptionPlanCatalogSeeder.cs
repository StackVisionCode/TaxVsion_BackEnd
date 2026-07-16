using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Siembra el catálogo inicial de planes (Starter/Pro/Enterprise) en el arranque del
/// servicio. Idempotente: no hace nada si ya existe al menos un plan. Construye cada
/// plan a través de su API de dominio pública (Create, AddVersion, AddFeature,
/// AddEntitlementDefinition, AddPriceTier, PublishVersion) — no escribe filas crudas,
/// así que las invariantes del aggregate se ejercitan igual que en producción.
/// </summary>
public static class SubscriptionPlanCatalogSeeder
{
    public static async Task SeedAsync(SubscriptionDbContext db, CancellationToken ct)
    {
        if (await db.Plans.AnyAsync(ct))
            return;

        var nowUtc = DateTime.UtcNow;

        db.Plans.Add(BuildStarterPlan(nowUtc));
        db.Plans.Add(BuildProPlan(nowUtc));
        db.Plans.Add(BuildEnterprisePlan(nowUtc));

        await db.SaveChangesAsync(ct);
    }

    private static SubscriptionPlan BuildStarterPlan(DateTime nowUtc) =>
        BuildPlan(
            planId: PlanCatalog.StarterId,
            versionId: PlanCatalog.StarterV1Id,
            code: PlanCatalog.Starter,
            name: "Starter",
            description: "Para oficinas que estan empezando: 3 usuarios, clientes, firmas y documentos.",
            tier: PlanTier.Standard,
            monthlyPriceUsd: 49m,
            seatsMax: 3,
            maxPendingInvitations: 5,
            storageQuotaBytes: 10L * 1024 * 1024 * 1024,
            modules: ["customers", "signatures", "documents", "planner"],
            nowUtc: nowUtc);

    private static SubscriptionPlan BuildProPlan(DateTime nowUtc) =>
        BuildPlan(
            planId: PlanCatalog.ProId,
            versionId: PlanCatalog.ProV1Id,
            code: PlanCatalog.Pro,
            name: "Pro",
            description: "Para oficinas en crecimiento: 10 usuarios, correo, comunicacion y campanas.",
            tier: PlanTier.Pro,
            monthlyPriceUsd: 129m,
            seatsMax: 10,
            maxPendingInvitations: 15,
            storageQuotaBytes: 50L * 1024 * 1024 * 1024,
            modules: ["customers", "signatures", "documents", "planner", "email", "comms", "campaigns", "reports"],
            nowUtc: nowUtc);

    private static SubscriptionPlan BuildEnterprisePlan(DateTime nowUtc) =>
        BuildPlan(
            planId: PlanCatalog.EnterpriseId,
            versionId: PlanCatalog.EnterpriseV1Id,
            code: PlanCatalog.Enterprise,
            name: "Enterprise",
            description: "Para multiservices con equipos grandes: 25 usuarios y todos los modulos.",
            tier: PlanTier.Enterprise,
            monthlyPriceUsd: 299m,
            seatsMax: 25,
            maxPendingInvitations: 40,
            storageQuotaBytes: 200L * 1024 * 1024 * 1024,
            modules:
            [
                "customers", "signatures", "documents", "planner", "email", "comms",
                "campaigns", "reports", "marketing", "builder", "irs", "miles",
            ],
            nowUtc: nowUtc);

    private static SubscriptionPlan BuildPlan(
        Guid planId,
        Guid versionId,
        string code,
        string name,
        string description,
        PlanTier tier,
        decimal monthlyPriceUsd,
        int seatsMax,
        int maxPendingInvitations,
        long storageQuotaBytes,
        IReadOnlyCollection<string> modules,
        DateTime nowUtc)
    {
        var plan = SubscriptionPlan.Seed(planId, PlanCode.Create(code).Value, name, description, tier, nowUtc).Value;

        var version = SubscriptionPlanVersion
            .Seed(versionId, planId, versionNumber: 1, trialDaysDefault: 14, supportedBillingCycles: [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;

        AddEntitlement(version, "seats.max", EntitlementValueType.Int, seatsMax.ToString());
        AddEntitlement(version, "invitations.max_pending", EntitlementValueType.Int, maxPendingInvitations.ToString());
        AddEntitlement(version, "storage.max_bytes", EntitlementValueType.Long, storageQuotaBytes.ToString());

        foreach (var module in modules)
            AddFeature(version, $"module.{module}");

        var monthlyTier = PlanPriceTier
            .Create(versionId, BillingCycle.Monthly, minQuantity: 1, maxQuantity: null, Money.Create(monthlyPriceUsd, "USD").Value)
            .Value;
        version.AddPriceTier(monthlyTier);

        // Anual = 10x el mensual ("2 meses gratis", convencion estandar SaaS) — ajustar si el
        // negocio define otro descuento.
        var yearlyTier = PlanPriceTier
            .Create(versionId, BillingCycle.Yearly, minQuantity: 1, maxQuantity: null, Money.Create(monthlyPriceUsd * 10m, "USD").Value)
            .Value;
        version.AddPriceTier(yearlyTier);

        plan.AddVersion(version, actorUserId: Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, actorUserId: Guid.Empty, nowUtc);

        return plan;
    }

    private static void AddEntitlement(SubscriptionPlanVersion version, string key, EntitlementValueType valueType, string defaultValue)
    {
        var entitlement = PlanEntitlementDefinition
            .Create(version.Id, EntitlementKey.Create(key).Value, valueType, defaultValue, description: key)
            .Value;
        version.AddEntitlementDefinition(entitlement);
    }

    private static void AddFeature(SubscriptionPlanVersion version, string key)
    {
        var feature = PlanFeature.Create(version.Id, EntitlementKey.Create(key).Value, defaultEnabled: true, description: key).Value;
        version.AddFeature(feature);
    }
}
