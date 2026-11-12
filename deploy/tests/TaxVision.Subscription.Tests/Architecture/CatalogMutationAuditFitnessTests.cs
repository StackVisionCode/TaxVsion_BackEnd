using System.Reflection;
using TaxVision.Subscription.Application.AddOns.Commands.CreateQuotaAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Catalog.Commands.CreateModule;
using TaxVision.Subscription.Application.Catalog.Commands.UpdateModule;
using TaxVision.Subscription.Application.Catalog.Migrations;
using TaxVision.Subscription.Application.Plans.Commands.CreateDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.CreatePlan;
using TaxVision.Subscription.Application.Plans.Commands.DeprecatePlan;
using TaxVision.Subscription.Application.Plans.Commands.PlanSales;
using TaxVision.Subscription.Application.Plans.Commands.PublishDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.ReplaceDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;
using TaxVision.Subscription.Application.Plans.Commands.UpdatePlanMetadata;
using TaxVision.Subscription.Application.RateLimiting.Commands.ReviseDefaultRateLimit;
using TaxVision.Subscription.Application.RateLimiting.Commands.UpsertPlanRateLimit;
using TaxVision.Subscription.Application.RateLimiting.DomainEvents;
using TaxVision.Subscription.Application.Seats.Commands.SetSeatPrices;

namespace TaxVision.Subscription.Tests.Architecture;

/// <summary>
/// D13: toda mutación del catálogo queda en la auditoría de plataforma. Cada handler que cambia planes, ediciones,
/// add-ons, módulos, rate limits o precios de asiento recibe <see cref="ICatalogAuditLog"/>; uno nuevo sin ella
/// falla acá (y agregarlo a esta lista es parte de crearlo).
/// </summary>
public sealed class CatalogMutationAuditFitnessTests
{
    public static TheoryData<Type> CatalogMutationHandlers =>
        [
            typeof(CreatePlanHandler),
            typeof(UpdatePlanMetadataHandler),
            typeof(SetPlanModulesHandler),
            typeof(CreateDraftVersionHandler),
            typeof(ReplaceDraftVersionHandler),
            typeof(PublishDraftVersionHandler),
            typeof(DeprecatePlanHandler),
            typeof(ArchivePlanHandler),
            typeof(SetPlanPricesHandler),
            typeof(PutPlanOnSaleHandler),
            typeof(WithdrawPlanFromSaleHandler),
            typeof(SetPlanSalesRankHandler),
            typeof(SetPlanFeaturedHandler),
            typeof(CreateQuotaAddOnHandler),
            typeof(SetAddOnPricesHandler),
            typeof(CreateModuleHandler),
            typeof(UpdateModuleHandler),
            typeof(UpsertPlanRateLimitHandler),
            typeof(SetSeatPricesHandler),
            typeof(ReviseDefaultRateLimitHandler),
            typeof(StartSubscriberMigrationHandler),
            typeof(CancelSubscriberMigrationHandler),
            typeof(PlanCreatedDomainEventHandler),
        ];

    [Theory]
    [MemberData(nameof(CatalogMutationHandlers))]
    public void Every_catalog_mutation_writes_the_platform_audit(Type handler)
    {
        var handle = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(handle);
        Assert.Contains(handle!.GetParameters(), parameter => parameter.ParameterType == typeof(ICatalogAuditLog));
    }
}
