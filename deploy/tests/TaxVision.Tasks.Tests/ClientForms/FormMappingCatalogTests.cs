using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.Api.Controllers.ClientForms;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Mapping.Queries;
using TaxVision.Tasks.Infrastructure.ClientForms.Mapping;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormMappingCatalogTests
{
    [Fact]
    public void Staff_catalog_hides_admin_only_fiscal_targets()
    {
        var staff = Catalog(FormTemplateHandlerTestDoubles.Publisher);
        var admin = Catalog(FormMappingTestDefinitions.Admin);

        Assert.DoesNotContain(staff.Targets, target => target.Entity == "FiscalProfile");
        Assert.Contains(admin.Targets, target => target.TargetKey == "customer.fiscal.tax_identifier");
        Assert.Equal(CustomerDataCollectionTargets.CatalogVersion, staff.CatalogVersion);
        Assert.Equal(2, staff.Operations.Count);
    }

    [Fact]
    public void Caller_without_target_permissions_gets_an_empty_catalog()
    {
        var empty = Catalog(FormMappingTestDefinitions.NoPermissions);

        Assert.Empty(empty.Targets);
        Assert.Empty(empty.Operations);
    }

    [Fact]
    public void Retired_targets_are_hidden_and_deprecated_ones_are_flagged()
    {
        var catalog = new ContractDataCollectionTargetCatalog(
            CustomerDataCollectionTargets.CatalogVersion,
            CustomerDataCollectionTargets
                .All.Select(target =>
                    target.TargetKey switch
                    {
                        "customer.person.middle_name" => target with { Status = DataCollectionTargetStatus.Retired },
                        "customer.business.dba" => target with { Status = DataCollectionTargetStatus.Deprecated },
                        _ => target,
                    }
                )
                .ToArray(),
            CustomerDataCollectionTargets.Operations
        );

        var response = GetFormMappingCatalogHandler.Handle(
            new GetFormMappingCatalogQuery(FormMappingTestDefinitions.Admin),
            catalog
        );

        Assert.DoesNotContain(response.Targets, target => target.TargetKey == "customer.person.middle_name");
        Assert.Equal(
            MappingTargetStatus.Deprecated,
            Assert.Single(response.Targets, target => target.TargetKey == "customer.business.dba").Status
        );
    }

    [Fact]
    public void Targets_expose_compatible_component_types_and_row_fields()
    {
        var response = Catalog(FormTemplateHandlerTestDoubles.Publisher);

        Assert.Equal(
            ["select", "radio"],
            Assert.Single(response.Targets, target => target.TargetKey == "customer.language").CompatibleComponentTypes
        );
        var dependents = Assert.Single(response.Targets, target => target.TargetKey == "customer.relation.dependents");
        Assert.Contains(dependents.Fields, field => field.Name == "first_name" && field.Required);
    }

    [Fact]
    public void Endpoint_requires_configure_actions_and_is_a_client_forms_endpoint()
    {
        var controller = typeof(FormMappingCatalogController);
        var action = controller.GetMethod(nameof(FormMappingCatalogController.Get))!;

        Assert.Equal("tasks/forms/mapping-catalog", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(controller.GetCustomAttribute<ClientFormsEndpointAttribute>());
        Assert.DoesNotContain(
            ActorType.CustomerPortal,
            controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        Assert.Equal(
            "perm:" + TasksPermissions.FormTemplatesConfigureActions,
            action.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
    }

    private static TaxVision.Tasks.ClientForms.Application.Mapping.Responses.FormMappingCatalogResponse Catalog(
        PublisherCapabilities capabilities
    ) =>
        GetFormMappingCatalogHandler.Handle(
            new GetFormMappingCatalogQuery(capabilities),
            FormTemplateHandlerTestDoubles.TargetCatalog()
        );
}
