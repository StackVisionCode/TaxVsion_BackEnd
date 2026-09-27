using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Siembra el catálogo de add-ons de módulo (<see cref="ModuleAddOnCatalog"/>) con precios armonizados
/// a los planes (anual = mensual × 10,
/// mismo criterio de "2 meses gratis"). Idempotente: no hace nada si ya existe un add-on. Construye vía
/// la API de dominio (Seed/AddFeature/AddPriceTier/Publish). Precios de arranque — PlatformAdmin los edita
/// por endpoint.
/// </summary>
public static class SubscriptionAddOnCatalogSeeder
{
    public static async Task SeedAsync(SubscriptionDbContext db, CancellationToken ct)
    {
        if (await db.AddOnDefinitions.AnyAsync(ct))
            return;

        var nowUtc = DateTime.UtcNow;
        // Se siembran TODOS, incluidos los que no se ofrecen: así una instalación nueva queda igual
        // que una existente después de reconciliar, y el día que la feature exista solo hay que
        // volver a publicarlo en vez de crear una definición nueva con otro id.
        foreach (var addOn in ModuleAddOnCatalog.All)
            db.AddOnDefinitions.Add(BuildDefinition(addOn, nowUtc));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Construye la definición desde la entrada del catálogo. Público porque lo necesita también el
    /// reconciliador de arranque: un add-on NUEVO no llega nunca a una base ya sembrada (este seeder
    /// solo actúa si la tabla está vacía), y duplicar la construcción sería la forma segura de que las
    /// dos versiones acabaran con precios o features distintos.
    /// </summary>
    public static AddOnDefinition BuildDefinition(ModuleAddOnDefinition addOn, DateTime nowUtc)
    {
        var definition = AddOnDefinition
            .Seed(
                addOn.Id,
                AddOnCode.Create(addOn.Code).Value,
                addOn.Name,
                $"Modulo {addOn.Name} a la carta.",
                category: "module",
                allowMultipleInstances: false,
                supportedBillingCycles: [BillingCycle.Monthly, BillingCycle.Yearly],
                nowUtc
            )
            .Value;

        definition.AddFeature(
            AddOnFeature
                .Create(definition.Id, EntitlementKey.Create($"module.{addOn.Module}").Value, enabled: true)
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(
                    definition.Id,
                    BillingCycle.Monthly,
                    minQuantity: 1,
                    maxQuantity: null,
                    Money.Create(addOn.MonthlyUsd, "USD").Value
                )
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(
                    definition.Id,
                    BillingCycle.Yearly,
                    minQuantity: 1,
                    maxQuantity: null,
                    Money.Create(addOn.MonthlyUsd * 10m, "USD").Value
                )
                .Value
        );
        // Un add-on sin nada construido detrás se queda en Draft: nunca llega a la tienda. El caso
        // contrario —una base donde YA se había publicado— lo resuelve el reconciliador de arranque.
        if (addOn.Offered)
            definition.Publish(Guid.Empty, nowUtc);

        return definition;
    }
}
