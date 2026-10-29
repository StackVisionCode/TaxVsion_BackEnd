using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class CustomerDataCollectionTargetsTests
{
    [Fact]
    public void V1_has_unique_stable_targets_and_excludes_unsynchronized_catalog_ids()
    {
        Assert.Equal(1, CustomerDataCollectionTargets.CatalogVersion);
        Assert.Equal(21, CustomerDataCollectionTargets.All.Count);
        Assert.Equal(
            CustomerDataCollectionTargets.All.Count,
            CustomerDataCollectionTargets
                .All.Select(target => target.TargetKey)
                .Distinct(StringComparer.Ordinal)
                .Count()
        );
        Assert.DoesNotContain(
            CustomerDataCollectionTargets.All,
            target => target.TargetKey.Contains("occupation", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            CustomerDataCollectionTargets.All,
            target => target.TargetKey.Contains("business_activity", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void Restricted_and_update_only_targets_expose_their_contract()
    {
        var taxId = Find("customer.fiscal.tax_identifier");
        var dependents = Find("customer.relation.dependents");

        Assert.Equal(DataCollectionSensitivity.Restricted, taxId.Sensitivity);
        Assert.Equal(DataCollectionTargetType.TaxId, taxId.DataType);
        Assert.True(taxId.RequiresTenantAdmin);
        Assert.Equal([DataCollectionOperation.UpdateCustomerProfile], taxId.Operations);
        Assert.Equal(["customers.manage"], taxId.RequiredPermissions);
        Assert.Equal([DataCollectionOperation.UpdateCustomerProfile], dependents.Operations);
    }

    [Fact]
    public void Create_requirements_are_explicit_by_customer_kind()
    {
        Assert.Contains(
            Find("customer.kind").RequiredFor,
            requirement => requirement.Operation == DataCollectionOperation.CreateCustomer && requirement.Kind is null
        );
        Assert.Contains(
            Find("customer.person.first_name").RequiredFor,
            requirement => requirement.Kind == "Individual"
        );
        Assert.Contains(
            Find("customer.business.legal_name").RequiredFor,
            requirement => requirement.Kind == "Business"
        );
        Assert.Contains(
            Find("customer.primary_email").RequiredFor,
            requirement => requirement.Operation == DataCollectionOperation.CreateCustomer
        );
    }

    private static DataCollectionTarget Find(string key) =>
        Assert.Single(CustomerDataCollectionTargets.All, target => target.TargetKey == key);

    [Fact]
    public void Dependents_declare_row_fields_and_operations_declare_permissions()
    {
        var dependents = Find("customer.relation.dependents");
        var create = Assert.Single(
            CustomerDataCollectionTargets.Operations,
            operation => operation.Operation == DataCollectionOperation.CreateCustomer
        );

        Assert.Contains(dependents.Fields!, field => field.Name == "first_name" && field.Required);
        Assert.Contains(dependents.Fields!, field => field.Name == "last_name" && field.Required);
        Assert.Equal("customer.kind", create.DiscriminatorTargetKey);
        Assert.Equal("customer.language", create.ConfigTargets["language"]);
        Assert.All(
            CustomerDataCollectionTargets.Operations,
            operation => Assert.Equal(["customers.manage"], operation.RequiredPermissions)
        );
        Assert.All(CustomerDataCollectionTargets.All, target => Assert.Null(target.LegacyValueMaps));
    }

    [Fact]
    public void Adapter_translates_every_contract_value_to_the_engine_port()
    {
        var targets = Enum.GetValues<DataCollectionTargetType>()
            .Select(type => Find("customer.kind") with { TargetKey = $"customer.t_{type}", DataType = type })
            .Concat(
                Enum.GetValues<DataCollectionTargetStatus>()
                    .Select(status =>
                        Find("customer.kind") with
                        {
                            TargetKey = $"customer.s_{status}",
                            Status = status,
                        }
                    )
            )
            .Concat(
                Enum.GetValues<DataCollectionSensitivity>()
                    .Select(sensitivity =>
                        Find("customer.kind") with
                        {
                            TargetKey = $"customer.v_{sensitivity}",
                            Sensitivity = sensitivity,
                        }
                    )
            )
            .ToArray();

        var snapshot = new TaxVision.Tasks.Infrastructure.ClientForms.Mapping.ContractDataCollectionTargetCatalog(
            1,
            targets,
            CustomerDataCollectionTargets.Operations
        ).GetSnapshot();

        Assert.Equal(targets.Length, snapshot.Targets.Count);
        Assert.Equal(
            ["CreateCustomer", "UpdateCustomerProfile"],
            snapshot.Operations.Select(operation => operation.Name)
        );
    }
}
