using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Siembra el catálogo de add-ons de módulo con precios armonizados a los planes (anual = mensual × 10,
/// mismo criterio de "2 meses gratis"). Idempotente: no hace nada si ya existe un add-on. Construye vía
/// la API de dominio (Seed/AddFeature/AddPriceTier/Publish). Precios de arranque — PlatformAdmin los edita
/// por endpoint.
/// </summary>
public static class SubscriptionAddOnCatalogSeeder
{
    // (Id fijo, código, nombre, módulo que habilita, precio mensual USD). à la carte por encima del
    // precio "por módulo" del bundle → incentiva el upgrade de plan.
    private static readonly (Guid Id, string Code, string Name, string Module, decimal MonthlyUsd)[] AddOns =
    [
        (new Guid("d1000000-0000-0000-0000-000000000001"), "addon-email", "Correo", "email", 29m),
        (new Guid("d1000000-0000-0000-0000-000000000002"), "addon-comms", "Comunicacion", "comms", 29m),
        (new Guid("d1000000-0000-0000-0000-000000000003"), "addon-campaigns", "Campanas", "campaigns", 29m),
        (new Guid("d1000000-0000-0000-0000-000000000004"), "addon-reports", "Reportes", "reports", 29m),
        (new Guid("d1000000-0000-0000-0000-000000000005"), "addon-marketing", "Marketing", "marketing", 49m),
        (new Guid("d1000000-0000-0000-0000-000000000006"), "addon-builder", "Builder", "builder", 49m),
        (new Guid("d1000000-0000-0000-0000-000000000007"), "addon-irs", "IRS", "irs", 49m),
        (new Guid("d1000000-0000-0000-0000-000000000008"), "addon-miles", "Millas", "miles", 49m),
    ];

    public static async Task SeedAsync(SubscriptionDbContext db, CancellationToken ct)
    {
        if (await db.AddOnDefinitions.AnyAsync(ct))
            return;

        var nowUtc = DateTime.UtcNow;
        foreach (var addOn in AddOns)
            db.AddOnDefinitions.Add(Build(addOn, nowUtc));

        await db.SaveChangesAsync(ct);
    }

    private static AddOnDefinition Build(
        (Guid Id, string Code, string Name, string Module, decimal MonthlyUsd) addOn,
        DateTime nowUtc
    )
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
        definition.Publish(Guid.Empty, nowUtc);
        return definition;
    }
}
