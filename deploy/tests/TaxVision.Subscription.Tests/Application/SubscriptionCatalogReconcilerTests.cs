using TaxVision.Subscription.Api.Bootstrap;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// El reconciliador de catálogo pone al día una instalación que YA existe (los seeders solo actúan
/// contra una base vacía). Lo que se fija acá es la propiedad de la que depende todo lo demás:
/// **compara antes de actuar**.
///
/// <para><c>SubscriptionPlan.ReviseModules</c> publica una versión nueva de forma incondicional
/// (<c>published.VersionNumber + 1</c>), sin mirar si los módulos cambiaron. Sin la comparación, cada
/// arranque publicaría v2, v3, v4… y cada una dispararía un recálculo masivo de todos los tenants del
/// plan. En producción, con reinicios por despliegue o por escalado, sería un goteo permanente.</para>
/// </summary>
public sealed class SubscriptionCatalogReconcilerTests
{
    [Fact]
    public void A_plan_that_already_matches_the_catalog_is_left_alone()
    {
        var plan = PublishedPlanWith("customers", "documents");

        var current = SubscriptionCatalogReconciler.PublishedModulesOf(plan);

        Assert.NotNull(current);
        Assert.False(SubscriptionCatalogReconciler.NeedsRevision(current!, ["customers", "documents"]));
    }

    [Fact]
    public void The_order_of_the_modules_is_not_a_difference()
    {
        // Si el orden contara, el primer arranque tras un cambio de orden en el código publicaría una
        // versión nueva sin que el plan hubiera cambiado en nada.
        var plan = PublishedPlanWith("customers", "documents", "planner");

        var current = SubscriptionCatalogReconciler.PublishedModulesOf(plan);

        Assert.False(SubscriptionCatalogReconciler.NeedsRevision(current!, ["planner", "customers", "documents"]));
    }

    [Fact]
    public void An_uppercase_module_key_cannot_even_exist()
    {
        // La comparación usa un comparador que ignora mayúsculas, pero no hace falta que lo haga: el
        // dominio ya lo impide antes (`EntitlementKey` exige ^[a-z][a-z0-9._]{2,99}$). Se deja escrito
        // acá porque el primer intento de este test asumía lo contrario y probaba un estado imposible.
        Assert.True(EntitlementKey.Create("module.Customers").IsFailure);
        Assert.True(EntitlementKey.Create("module.customers").IsSuccess);
    }

    [Fact]
    public void Removing_a_module_IS_a_difference()
    {
        // El caso real de este cambio: Pro tenía `reports` y deja de tenerlo.
        var plan = PublishedPlanWith("customers", "documents", "reports");

        var current = SubscriptionCatalogReconciler.PublishedModulesOf(plan);

        Assert.True(SubscriptionCatalogReconciler.NeedsRevision(current!, ["customers", "documents"]));
    }

    [Fact]
    public void Adding_a_module_IS_a_difference()
    {
        var plan = PublishedPlanWith("customers");

        var current = SubscriptionCatalogReconciler.PublishedModulesOf(plan);

        Assert.True(SubscriptionCatalogReconciler.NeedsRevision(current!, ["customers", "comms"]));
    }

    [Fact]
    public void Only_module_features_are_compared()
    {
        // Una versión de plan lleva también features que no son módulos; meterlas en la comparación
        // haría que el reconciliador viera una diferencia en cada arranque y no parara nunca.
        var plan = PublishedPlanWith("customers");
        var published = plan.Versions.Single();
        published.AddFeature(
            PlanFeature.Create(published.Id, EntitlementKey.Create("seats.max").Value, true, "seats.max").Value
        );

        var current = SubscriptionCatalogReconciler.PublishedModulesOf(plan);

        Assert.Equal(["customers"], current!.Order(StringComparer.Ordinal));
        Assert.False(SubscriptionCatalogReconciler.NeedsRevision(current, ["customers"]));
    }

    [Fact]
    public void A_plan_without_a_published_version_is_reported_not_revised()
    {
        var plan = SubscriptionPlan
            .Create(
                PlanCode.Create("starter").Value,
                "Starter",
                "Starter",
                PlanTier.Standard,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        Assert.Null(SubscriptionCatalogReconciler.PublishedModulesOf(plan));
    }

    // ---------- El catálogo en sí ----------

    [Fact]
    public void No_plan_sells_a_module_with_nothing_built_behind_it()
    {
        // `reports`, `marketing`, `builder`, `irs` y `miles` no tienen ni un endpoint que los exija ni
        // pantalla en el CRM. Un plan que los promete cobra por algo que no entrega.
        string[] notBuilt = ["reports", "marketing", "builder", "irs", "miles"];

        foreach (var (_, planCode, modules) in PlanModuleCatalog.All)
        {
            foreach (var module in notBuilt)
                Assert.False(
                    modules.Contains(module, StringComparer.OrdinalIgnoreCase),
                    $"El plan {planCode} sigue vendiendo '{module}', que no existe."
                );
        }
    }

    [Fact]
    public void The_add_ons_retired_for_not_existing_are_exactly_those_five()
    {
        Assert.Equal(
            ["addon-builder", "addon-irs", "addon-marketing", "addon-miles", "addon-reports"],
            ModuleAddOnCatalog.NotBuilt.Select(addOn => addOn.Code).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void The_only_add_on_retired_for_being_included_everywhere_is_comms()
    {
        // Razón distinta de las otras cinco y conviene no mezclarlas: `comms` existe y funciona, pero
        // su módulo pasó a TODOS los planes, así que ya no hay a quién vendérselo.
        Assert.Equal(
            ["addon-comms"],
            ModuleAddOnCatalog
                .All.Where(addOn => addOn.Availability == AddOnAvailability.IncludedInEveryPlan)
                .Select(addOn => addOn.Code)
        );

        foreach (
            var module in ModuleAddOnCatalog
                .All.Where(addOn => addOn.Availability == AddOnAvailability.IncludedInEveryPlan)
                .Select(addOn => addOn.Module)
        )
        {
            Assert.Contains(module, PlanModuleCatalog.Starter, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(module, PlanModuleCatalog.Pro, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(module, PlanModuleCatalog.Enterprise, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_add_on_ON_SALE_maps_to_a_module_the_gate_knows()
    {
        // Un add-on que habilita un `module.*` que el mapa no conoce cobra por algo que no gatea nada:
        // el tenant paga y el backend no le protege ni le habilita nada distinto.
        //
        // Solo se exige a los que se OFRECEN. `marketing`, `builder`, `irs` y `miles` están fuera del
        // mapa a propósito (no tienen endpoints) — y esa es justamente la razón por la que dejan de
        // venderse. El primer intento de este test los incluía y falló: el test encontró el problema
        // antes de que lo encontrara un cliente.
        foreach (var addOn in ModuleAddOnCatalog.All.Where(addOn => addOn.Offered))
            Assert.Contains(addOn.Module, BuildingBlocks.Authorization.PermissionModuleMap.KnownModules);
    }

    [Fact]
    public void An_add_on_retired_for_not_existing_is_one_the_gate_cannot_enforce()
    {
        // La otra dirección, para que la lista de retirados no crezca por descuido: si algo se retira
        // POR NO EXISTIR, el gate tampoco debería conocer su módulo. `reports` es la excepción
        // documentada — SÍ está en el mapa, pero su único permiso (`reports.view`) no lo exige ningún
        // endpoint. Los retirados por estar incluidos en todos los planes no entran acá: ésos sí
        // existen.
        foreach (var addOn in ModuleAddOnCatalog.NotBuilt.Where(addOn => addOn.Module != "reports"))
            Assert.DoesNotContain(addOn.Module, BuildingBlocks.Authorization.PermissionModuleMap.KnownModules);
    }

    [Fact]
    public void Every_module_sold_in_a_plan_is_offered_as_an_add_on()
    {
        // La escalera comercial: lo que trae un plan superior se puede comprar suelto desde uno
        // inferior. Si falta, un Starter no tiene forma de conseguir ese módulo sin cambiar de plan.
        var offered = ModuleAddOnCatalog
            .All.Where(addOn => addOn.Offered)
            .Select(addOn => addOn.Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in PlanModuleCatalog.Enterprise.Except(PlanModuleCatalog.Starter))
            Assert.True(offered.Contains(module), $"El módulo '{module}' no se puede comprar suelto.");

        // Y al revés: lo que Starter ya trae no se vende suelto, porque no habría a quién.
        foreach (var module in PlanModuleCatalog.Starter)
            Assert.False(
                offered.Contains(module),
                $"El módulo '{module}' está en todos los planes y aun así se ofrece como add-on."
            );
    }

    private static SubscriptionPlan PublishedPlanWith(params string[] modules)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "Starter", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;

        foreach (var module in modules)
            version.AddFeature(
                PlanFeature
                    .Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, $"module.{module}")
                    .Value
            );

        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }
}
